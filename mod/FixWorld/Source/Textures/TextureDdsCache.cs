// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using FixWorld.ExternalTools;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FixWorld.Caching;
using FixWorld.Profiling;
using FixWorld.Telemetry;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.Textures
{
    internal sealed class TextureDdsCache : IDisposable
    {


        private const int MaintenanceIntervalSeconds = 30;

        private readonly object sync = new object();
        private readonly CacheStore caches;
        private readonly TypedCache<string, MemoryMappedFileSpanWrapper> startupReaders;
        private readonly List<MemoryMappedFileSpanWrapper> ownedReaders = new();
        private readonly TelemetryRegistration<TextureDdsCacheSnapshot> telemetry;
        private readonly ProfileSlot<ProfileKey> loadProfile, discoveryProfile, buildProfile;
        private readonly ConcurrentQueue<DdsBuildResult> completions = new();
        private readonly CancellationTokenSource cancellation = new();
        private bool workerActive;
        private int maintenanceRequested = 1;
        private long nextMaintenanceTimestamp;
        private long availableFreeBytes = -1;
        private long effectiveBudgetBytes;
        private string reserveWarning;
        private bool maximumOverridden, minimumFreeOverridden;
        private bool readersBound;
        private string lastError;
        private readonly HashSet<ModContentPack> observedTextureMods =
            new HashSet<ModContentPack>();
        private readonly HashSet<string> activePackages =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> usedPackages =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> retainedByPackage =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly List<DdsModPlan> discoveredBuildPlans =
            new List<DdsModPlan>();
        private readonly HashSet<string> rebuildSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DdsModPlan> failedBuildPlans =
            new Dictionary<string, DdsModPlan>(StringComparer.Ordinal);
        private readonly Dictionary<string, DdsPackSlice> startupHits =
            new Dictionary<string, DdsPackSlice>(
                StringComparer.OrdinalIgnoreCase);


        private DdsModPlan[] pendingBuildPlans = Array.Empty<DdsModPlan>();
        private DdsPackSnapshot discoverySnapshot;

        private string cacheRoot;
        private string texconvPath;
        private string converterIdentity;
        private long maximumCacheBytes;
        private long minimumFreeBytes;
        private const int workerCount = 1;
        private DdsPackStore store;
        private bool attached;
        private bool backgroundStarted;
        private bool observingTextureDiscovery;
        private bool stopped;
        private int scheduledBuilds;
        private int completedBuilds;
        private int plannedMods, processedMods;
        private string currentMod;

        private long hits;
        private long misses;
        private long created;
        private long invalidated;
        private long excluded;
        private long unsupported;
        private long budgetSkipped;
        private long failed;
        private long buildMilliseconds;
        private long cacheBytes;
        private long workerPreparedMods;
        private long workerAppliedMods;
        private long workerFallbackMods;

        internal TextureDdsCache(CacheStore caches, LibraryDiagnostics diagnostics)
        {
            this.caches = caches;
            startupReaders = caches.Create(new CacheContract<string, MemoryMappedFileSpanWrapper>(
                "dds.pack-readers", 8192, path =>
                {
                    var reader = new MemoryMappedFileSpanWrapper(path);
                    ownedReaders.Add(reader);
                    return reader;
                }, StringComparer.OrdinalIgnoreCase));
            telemetry = diagnostics.Store.Register(TextureDdsCacheSnapshot.Contract);
            loadProfile = diagnostics.Profiler.GetSlot(new ProfileKey("dds", "upload"));
            discoveryProfile = diagnostics.Profiler.GetSlot(new ProfileKey("dds", "discovery"));
            buildProfile = diagnostics.Profiler.GetSlot(new ProfileKey("dds", "build"));
            Publish();
        }

        internal DdsSettings Settings { get; private set; }
        internal TextureDdsCacheSnapshot PublishedSnapshot => telemetry.Snapshot;
        internal void RegisterSettings(FixWorld.Settings.ModSettingsPack pack)
        {
            if (Settings != null)
                return;
            Settings = new DdsSettings(pack, ApplySettings);
            ApplySettings();
        }

        private void ApplySettings()
        {
            try
            {
                long maximum = Settings.EffectiveMaximumBytes;
                long reserve = Settings.EffectiveMinimumFreeBytes;
                Interlocked.Exchange(ref maximumCacheBytes, maximum);
                Interlocked.Exchange(ref minimumFreeBytes, reserve);
                maximumOverridden = Settings.MaximumOverridden;
                minimumFreeOverridden = Settings.MinimumFreeOverridden;
                Interlocked.Exchange(ref maintenanceRequested, 1);
            }
            catch (Exception error) { lastError = error.Message; }
        }

        internal bool Busy { get { lock (sync) return workerActive; } }
        internal bool CanMaintain { get { lock (sync) return IsRunning && backgroundStarted && !Busy; } }
        internal void BeginBackgroundIfReady()
        {
            if (!attached || !IsRunning)
                return;
            if (backgroundStarted)
            {
                lock (sync)
                {
                    if (!workerActive && (Volatile.Read(ref maintenanceRequested) != 0 ||
                        Stopwatch.GetTimestamp() >= nextMaintenanceTimestamp))
                        ScheduleBuilds(Array.Empty<DdsModPlan>());
                }
                return;
            }
            try
            {
                CompleteLoading();
                StartBackgroundBuild();
            }
            catch (Exception error)
            {
                backgroundStarted = true;
                observingTextureDiscovery = false;
                lastError = error.Message;
                Interlocked.Increment(ref failed);
                DisposeStartupReaders();
                Log.Warning("[FixWorld] DDS background startup failed, gameplay continues: " + error);
            }
        }
        internal void Publish()
        {
            while (completions.TryDequeue(out var result))
                CompleteBuild(result);
            telemetry.Publish(GetSnapshot());
        }

        internal void Attach(string modRoot)
        {
            lock (sync)
            {
                if (attached)
                {
                    return;
                }

                attached = true;
                if (!Configure())
                {
                    Log.Message("[FixWorld] DDS pack cache disabled.");
                    return;
                }

                try
                {
                    texconvPath = TexconvProcess.FindExecutable(Path.Combine(
                        modRoot,
                        "Tools",
                        "Windows-x64"));
                    converterIdentity = File.Exists(texconvPath)
                        ? "sha256:" + DdsCacheKey.HashFile(texconvPath)
                        : null;
                    store = DdsPackStore.Open(
                        cacheRoot,
                        DdsCacheContract.CacheIdentityVersion);
                    SetCacheBytes(store.CurrentBytes);
                    LogConfiguration();
                }
                catch (Exception exception)
                {
                    store?.Dispose();
                    store = null;
                    Log.Warning(
                        "[FixWorld] DDS pack cache disabled after " +
                        "initialization failure: " + exception);
                }
            }
        }



        internal void Prepare()
        {
            lock (sync)
            {
                if (!IsRunning)
                {
                    return;
                }

                if (backgroundStarted || discoverySnapshot != null)
                    return;
                ResetDiscovery();
                discoverySnapshot = store.Snapshot();
                foreach (ModContentPack mod in
                         LoadedModManager.RunningModsListForReading)
                {
                    activePackages.Add(DdsCacheKey.Normalize(mod.PackageId));
                }
            }
        }

        internal void BeginTextureDiscovery()
        {
            lock (sync)
            {
                observingTextureDiscovery =
                    IsRunning && discoverySnapshot != null;
            }
        }

        internal void StartBackgroundBuild()
        {
            lock (sync)
            {
                if (!IsRunning || backgroundStarted)
                {
                    return;
                }

                backgroundStarted = true;
                Log.Message(
                    "[FixWorld] DDS pack background stage started; queuedMods=" +
                    pendingBuildPlans.Length.ToString(
                        CultureInfo.InvariantCulture) + ".");
                ScheduleBuilds(pendingBuildPlans);
                pendingBuildPlans = Array.Empty<DdsModPlan>();
            }
        }

        internal bool TryLoad(VirtualFile source, out Texture2D texture)
        {
            texture = null;
            if (source == null || string.IsNullOrWhiteSpace(source.FullPath))
            {
                return false;
            }

            lock (sync)
            {
                if (!startupHits.TryGetValue(
                        Path.GetFullPath(source.FullPath),
                        out DdsPackSlice slice))
                {
                    return false;
                }

                try
                {
                    caches.BindCurrentThread();
                    readersBound = true;
                    var reader = startupReaders.GetOrAdd(slice.Path);
                    using var measurement = loadProfile.Measure();

                    texture = DdsPackTextureLoader.Load(
                        reader,
                        slice,
                        source.Name);
                    Interlocked.Increment(ref hits);
                    return true;
                }
                catch (Exception exception)
                {
                    startupHits.Remove(Path.GetFullPath(source.FullPath));
                    rebuildSources.Add(Path.GetFullPath(source.FullPath));
                    Interlocked.Increment(ref failed);
                    Log.Warning(
                        "[FixWorld] Packed DDS load fell back to RimWorld for " +
                        source.FullPath + ": " + exception.Message);
                    return false;
                }
            }
        }

        internal void ObserveTextureFiles(
            ModContentPack mod,
            string contentPath,
            Func<string, bool> validateExtension,
            List<string> foldersToLoadDebug,
            Dictionary<string, FileInfo> files)
        {
            if (mod == null ||
                files == null ||
                foldersToLoadDebug != null ||
                !string.Equals(
                    contentPath,
                    GenFilePaths.ContentPath<Texture2D>(),
                    StringComparison.Ordinal) ||
                validateExtension?.Method.DeclaringType !=
                typeof(ModContentLoader<Texture2D>) ||
                !string.Equals(
                    validateExtension.Method.Name,
                    nameof(ModContentLoader<Texture2D>.IsAcceptableExtension),
                    StringComparison.Ordinal))
            {
                return;
            }

            lock (sync)
            {
                if (!observingTextureDiscovery ||
                    discoverySnapshot == null ||
                    !observedTextureMods.Add(mod))
                {
                    return;
                }

                using var measurement = discoveryProfile.Measure();
                DdsModPlan plan = CreatePlan(
                    mod,
                    discoverySnapshot,
                    files);
                foreach (DdsPackItem item in plan.Items)
                {
                    if (item.HasExisting)
                    {
                        startupHits[item.Source.FullName] = item.Existing;
                    }
                }

                Interlocked.Increment(ref workerPreparedMods);
                if (plan.Hits.Count > 0)
                {
                    usedPackages.Add(plan.PackageId);
                    Interlocked.Increment(ref workerAppliedMods);
                }
                else
                {
                    Interlocked.Increment(ref workerFallbackMods);
                }

                Interlocked.Add(ref misses, plan.MissingCount);
                Interlocked.Add(ref excluded, plan.Excluded);
                Interlocked.Add(ref unsupported, plan.Unsupported);

                retainedByPackage[plan.PackageId] = new HashSet<string>(
                    plan.Items.Select(item => item.SourcePath),
                    StringComparer.Ordinal);
                if (string.IsNullOrEmpty(texconvPath))
                {
                    Interlocked.Add(ref budgetSkipped, plan.MissingCount);
                    return;
                }

                discoveredBuildPlans.Add(plan);
            }
        }

        internal void CompleteLoading()
        {
            lock (sync)
            {
                FinalizeDiscovery();
                DisposeStartupReaders();
                startupHits.Clear();
            }
        }

        internal TextureDdsCacheSnapshot GetSnapshot()
        {
            lock (sync)
            {
                return GetSnapshotCore(IsRunning);
            }
        }

        internal string ClearCache()
        {
            lock (sync)
            {
                if (!CanMaintain)
                    return "Wait until loading and DDS background work have finished.";
                try
                {
                    DisposeStartupReaders();
                    startupHits.Clear();
                    ScheduleBuilds(Array.Empty<DdsModPlan>(), clearCache: true);
                    return "DDS cache cleanup queued. Restart RimWorld after it finishes to rebuild the cache.";
                }
                catch (Exception error)
                {
                    lastError = error.Message;
                    return "DDS cache could not be fully cleared: " + lastError;
                }
            }
        }

        internal string RetryFailedBuilds()
        {
            lock (sync)
            {
                if (!CanMaintain)
                    return "Wait until loading and DDS background work have finished.";

                if (Busy)
                {
                    return "DDS background work is already running.";
                }

                if (failedBuildPlans.Count == 0)
                {
                    return "There are no failed DDS builds to retry.";
                }

                DdsModPlan[] retryPlans = failedBuildPlans.Values.ToArray();
                failedBuildPlans.Clear();

                ScheduleBuilds(retryPlans);
                Log.Message(
                    "[FixWorld] Retrying failed DDS pack builds; queuedMods=" +
                    retryPlans.Length.ToString(CultureInfo.InvariantCulture) + ".");
                return "Retry started for " +
                       retryPlans.Length.ToString(CultureInfo.InvariantCulture) +
                       (retryPlans.Length == 1 ? " mod." : " mods.");
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (stopped)
                    return;
                stopped = true;
                Settings?.Dispose();
                cancellation.Cancel();
                startupHits.Clear();
                DisposeStartupReaders();
                ResetDiscovery();
                telemetry.Dispose();
                startupReaders.Dispose();
                // A worker may still be unwinding texconv/IO. It owns final store
                // disposal in its finally block, never race it with a timeout.
                if (!workerActive)
                    DisposeStore();
            }
        }

        private void DisposeStore()
        {
            try
            { store?.Dispose(); }
            finally { store = null; cancellation.Dispose(); }
        }

        private bool IsRunning => !stopped && store != null;

        private void FinalizeDiscovery()
        {
            observingTextureDiscovery = false;
            if (store == null || discoverySnapshot == null)
            {
                ResetDiscovery();
                return;
            }

            AddInvalidated(store.ReconcilePackages(retainedByPackage));
            store.TouchPackages(usedPackages);
            AddInvalidated(store.RemoveInactivePackages(activePackages));
            store.Save();
            SetCacheBytes(store.CurrentBytes);
            pendingBuildPlans = discoveredBuildPlans.Select(plan =>
            {
                var items = plan.Items.Select(item => item.HasExisting && rebuildSources.Contains(item.Source.FullName)
                    ? DdsPackItem.FromMissing(item.LogicalPath, item.SourcePath, item.Source, null, 0, item.Source.Length)
                    : item).ToArray();
                return new DdsModPlan(plan.PackageId, plan.Hits, items,
                    items.Count(item => !item.HasExisting), plan.Excluded, plan.Unsupported, plan.EstimatedPackBytes);
            }).Where(plan => plan.MissingCount > 0).ToArray();
            rebuildSources.Clear();
            discoverySnapshot = null;
            observedTextureMods.Clear();
            activePackages.Clear();
            usedPackages.Clear();
            retainedByPackage.Clear();
            discoveredBuildPlans.Clear();
        }

        private void ResetDiscovery()
        {
            observingTextureDiscovery = false;
            discoverySnapshot = null;
            observedTextureMods.Clear();
            activePackages.Clear();
            usedPackages.Clear();
            retainedByPackage.Clear();
            discoveredBuildPlans.Clear();
        }

        private void DisposeStartupReaders()
        {
            if (readersBound)
                startupReaders.Clear();
            foreach (var reader in ownedReaders)
                reader.Dispose();
            ownedReaders.Clear();
        }

        private DdsModPlan CreatePlan(
            ModContentPack mod,
            DdsPackSnapshot snapshot,
            Dictionary<string, FileInfo> discovered)
        {
            string packageId = DdsCacheKey.Normalize(mod.PackageId);
            string modRoot = Path.GetFullPath(mod.RootDir).TrimEnd(
                                 Path.DirectorySeparatorChar,
                                 Path.AltDirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
            HashSet<string> shippedDds = new HashSet<string>(
                discovered.Keys
                    .Select(DdsCacheKey.Normalize)
                    .Where(key => key.EndsWith(
                        ".dds",
                        StringComparison.Ordinal)),
                StringComparer.Ordinal);
            Dictionary<string, DdsPackSlice> cacheHits =
                new Dictionary<string, DdsPackSlice>(StringComparer.Ordinal);
            List<DdsPackItem> items = new List<DdsPackItem>();
            int missing = 0;
            int excludedCount = 0;
            int unsupportedCount = 0;
            long estimatedBytes = 16L;

            foreach (KeyValuePair<string, FileInfo> discoveredFile in discovered)
            {
                FileInfo source = discoveredFile.Value;
                string logicalKey = DdsCacheKey.Normalize(discoveredFile.Key);
                string extension = source.Extension.ToLowerInvariant();
                if (extension == ".dds" ||
                    shippedDds.Contains(Path.ChangeExtension(
                        logicalKey,
                        ".dds")) ||
                    !source.FullName.StartsWith(
                        modRoot,
                        StringComparison.OrdinalIgnoreCase) ||
                    extension != ".png" && extension != ".jpg" &&
                    extension != ".jpeg")
                {
                    continue;
                }

                string sourcePath = DdsCacheKey.RelativeSource(source, modRoot);
                if (snapshot.TryGetFresh(
                        packageId,
                        sourcePath,
                        source,
                        converterIdentity,
                        out DdsPackSlice fresh))
                {
                    cacheHits.Add(discoveredFile.Key, fresh);
                    items.Add(DdsPackItem.FromExisting(
                        discoveredFile.Key,
                        sourcePath,
                        source,
                        sourceHash: null,
                        fresh));
                    estimatedBytes += Align(fresh.Length);
                    continue;
                }

                items.Add(DdsPackItem.FromMissing(
                    discoveredFile.Key,
                    sourcePath,
                    source,
                    sourceHash: null,
                    mipCount: 0,
                    estimatedBytes: source.Length));
                estimatedBytes += Align(source.Length);
                missing++;
            }

            return new DdsModPlan(
                packageId,
                cacheHits,
                items,
                missing,
                excludedCount,
                unsupportedCount,
                estimatedBytes);
        }

        private void ScheduleBuilds(IReadOnlyList<DdsModPlan> buildPlans, bool clearCache = false)
        {
            if (workerActive)
                throw new InvalidOperationException("DDS worker is already active.");
            scheduledBuilds = buildPlans.Count;
            completedBuilds = 0;
            lastError = null;
            var plans = buildPlans.ToArray();
            if (plans.Length > 0)
            { plannedMods = plans.Length; processedMods = 0; currentMod = null; }
            workerActive = true;
            Task.Factory.StartNew(() =>
            {
                try
                {
                    Thread.CurrentThread.Priority = System.Threading.ThreadPriority.BelowNormal;
                    if (clearCache)
                    {
                        store.RemoveInactivePackages(new HashSet<string>(StringComparer.Ordinal));
                        store.Save();
                        store.SweepOrphans();
                        lock (sync)
                            failedBuildPlans.Clear();
                    }
                    MaintainStore();
                    foreach (var plan in plans)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (Volatile.Read(ref maintenanceRequested) != 0)
                            MaintainStore();
                        lock (sync)
                            currentMod = plan.PackageId;
                        BuildAndPublish(plan, cancellation.Token);
                        lock (sync)
                        { processedMods++; currentMod = null; }
                    }
                    MaintainStore();
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { completions.Enqueue(new DdsBuildResult(0, 1, 0, error.Message)); }
                finally
                {
                    lock (sync)
                    {
                        workerActive = false;
                        currentMod = null;
                        nextMaintenanceTimestamp = Stopwatch.GetTimestamp() + MaintenanceIntervalSeconds * Stopwatch.Frequency;
                        if (stopped)
                            DisposeStore();
                    }
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private DdsBuildResult BuildAndPublish(
            DdsModPlan plan,
            CancellationToken cancellationToken)
        {
            long startedAt = Stopwatch.GetTimestamp();
            DdsBuiltPack pack = null;
            string stagingRoot = null;
            try
            {
                plan = PrepareMissing(plan, cancellationToken);
                if (plan.MissingCount == 0)
                {
                    lock (sync)
                    {
                        failedBuildPlans.Remove(plan.PackageId);
                    }

                    DdsBuildResult empty = new DdsBuildResult(
                        0,
                        0,
                        ElapsedMilliseconds(startedAt),
                        null);
                    PostCompletion(empty);
                    return empty;
                }

                long temporaryBytes = checked(
                    plan.EstimatedPackBytes * 2L);
                long availableBytes = new DriveInfo(
                        Path.GetPathRoot(cacheRoot))
                    .AvailableFreeSpace;
                if (plan.EstimatedPackBytes > Interlocked.Read(ref maximumCacheBytes) ||
                    availableBytes - temporaryBytes < Interlocked.Read(ref minimumFreeBytes))
                {
                    Interlocked.Add(ref budgetSkipped, plan.MissingCount);
                    DdsBuildResult skipped = new DdsBuildResult(
                        0,
                        0,
                        ElapsedMilliseconds(startedAt),
                        null);
                    PostCompletion(skipped);
                    return skipped;
                }

                pack = BuildPack(
                    plan,
                    cancellationToken,
                    out stagingRoot);
                cancellationToken.ThrowIfCancellationRequested();
                store.Publish(pack);
                MaintainStore();
                lock (sync)
                    failedBuildPlans.Remove(plan.PackageId);

                DdsBuildResult result = new DdsBuildResult(
                    plan.MissingCount,
                    0,
                    ElapsedMilliseconds(startedAt),
                    null);
                PostCompletion(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                store?.Discard(pack);
                if (pack == null)
                    store?.DiscardStaging(stagingRoot);

                throw;
            }
            catch (Exception exception)
            {
                store?.Discard(pack);
                if (pack == null)
                    store?.DiscardStaging(stagingRoot);
                lock (sync)
                {
                    if (IsRunning)
                    {
                        failedBuildPlans[plan.PackageId] = plan;
                    }
                }

                DdsBuildResult result = new DdsBuildResult(
                    0,
                    plan.MissingCount,
                    ElapsedMilliseconds(startedAt),
                    exception.ToString());
                PostCompletion(result);
                return result;
            }
        }

        private DdsModPlan PrepareMissing(
            DdsModPlan plan,
            CancellationToken cancellationToken)
        {
            List<DdsPackItem> items = new List<DdsPackItem>(plan.Items.Count);
            long estimatedBytes = 16L;
            int missing = 0;
            int excludedCount = 0;
            int unsupportedCount = 0;
            foreach (DdsPackItem item in plan.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.HasExisting && File.Exists(item.Existing.Path))
                {
                    items.Add(item);
                    estimatedBytes += Align(item.Existing.Length);
                    continue;
                }

                if (!TextureDimensions.TryRead(
                        item.Source,
                        out TextureDimensions dimensions))
                {
                    unsupportedCount++;
                    continue;
                }

                int mipCount = dimensions.GetBlockCompressedMipCount();
                if (mipCount == 0)
                {
                    excludedCount++;
                    continue;
                }

                long estimated = dimensions.GetBc7FileSize(mipCount);
                items.Add(DdsPackItem.FromMissing(
                    item.LogicalPath,
                    item.SourcePath,
                    item.Source,
                    item.SourceHash,
                    mipCount,
                    estimated));
                estimatedBytes += Align(estimated);
                missing++;
            }

            Interlocked.Add(ref excluded, excludedCount);
            Interlocked.Add(ref unsupported, unsupportedCount);
            return new DdsModPlan(
                plan.PackageId,
                plan.Hits,
                items,
                missing,
                excludedCount,
                unsupportedCount,
                estimatedBytes);
        }

        private DdsBuiltPack BuildPack(
            DdsModPlan plan,
            CancellationToken cancellationToken,
            out string stagingRoot)
        {
            stagingRoot = store.CreateStagingRoot(plan.PackageId);

            Dictionary<DdsPackItem, string> converted =
                ConvertMissing(plan, stagingRoot, cancellationToken);
            string temporaryPack = Path.Combine(stagingRoot, "pack.tmp");
            List<DdsBuiltEntry> entries =
                new List<DdsBuiltEntry>(plan.Items.Count);
            Dictionary<string, MemoryMappedFileSpanWrapper> readers =
                new Dictionary<string, MemoryMappedFileSpanWrapper>(
                    StringComparer.OrdinalIgnoreCase);
            try
            {
                using (FileStream output = new FileStream(
                           temporaryPack,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           256 * 1024,
                           FileOptions.SequentialScan))
                {
                    output.Write(
                        new byte[]
                        {
                            70, 87, 68, 68, 83, 80, 75, 49,
                            1, 0, 0, 0, 0, 0, 0, 0
                        },
                        0,
                        16);
                    foreach (DdsPackItem item in plan.Items.OrderBy(
                                 value => value.SourcePath,
                                 StringComparer.Ordinal))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Align(output);
                        long offset = output.Position;
                        if (item.HasExisting)
                        {
                            if (!readers.TryGetValue(
                                    item.Existing.Path,
                                    out MemoryMappedFileSpanWrapper reader))
                            {
                                reader = new MemoryMappedFileSpanWrapper(
                                    item.Existing.Path);
                                readers.Add(item.Existing.Path, reader);
                            }

                            reader.CopyTo(
                                output,
                                item.Existing.Offset,
                                item.Existing.Length);
                        }
                        else
                        {
                            using (FileStream input = new FileStream(
                                       converted[item],
                                       FileMode.Open,
                                       FileAccess.Read,
                                       FileShare.Read,
                                       256 * 1024,
                                       FileOptions.SequentialScan))
                            {
                                input.CopyTo(output, 256 * 1024);
                            }
                        }

                        long length = output.Position - offset;
                        string sourceHash = item.SourceHash ??
                                            DdsCacheKey.HashFile(
                                                item.Source.FullName);
                        entries.Add(new DdsBuiltEntry(
                            item.SourcePath,
                            item.Source,
                            sourceHash,
                            converterIdentity,
                            offset,
                            length));
                    }

                    output.Flush(true);
                }

                return new DdsBuiltPack(
                    plan.PackageId,
                    plan.Generation + "-" + Guid.NewGuid().ToString("N"),
                    stagingRoot,
                    temporaryPack,
                    entries);
            }
            finally
            {
                foreach (MemoryMappedFileSpanWrapper reader in readers.Values)
                {
                    reader.Dispose();
                }
            }
        }

        private Dictionary<DdsPackItem, string> ConvertMissing(
            DdsModPlan plan,
            string stagingRoot,
            CancellationToken cancellationToken)
        {
            DdsPackItem[] missingItems = plan.Items
                .Where(item => !item.HasExisting)
                .ToArray();
            Dictionary<DdsPackItem, string> outputs =
                new Dictionary<DdsPackItem, string>();
            Dictionary<string, int> occurrences =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<IGrouping<string, DdsConversion>> groups = missingItems
                .Select(item =>
                {
                    string name = Path.GetFileNameWithoutExtension(
                        item.Source.Name);
                    string occurrenceKey = item.MipCount.ToString(
                                               CultureInfo.InvariantCulture) +
                                           "\n" + name;
                    occurrences.TryGetValue(occurrenceKey, out int occurrence);
                    occurrences[occurrenceKey] = occurrence + 1;
                    string group = item.MipCount.ToString(
                                       CultureInfo.InvariantCulture) +
                                   "-" + occurrence.ToString(
                                       CultureInfo.InvariantCulture);
                    return new DdsConversion(item, name, group);
                })
                .GroupBy(item => item.Group, StringComparer.Ordinal);

            foreach (IGrouping<string, DdsConversion> group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string outputDirectory = Path.Combine(
                    stagingRoot,
                    "converted",
                    group.Key);
                DdsConversion[] batch = group.ToArray();
                TexconvProcessResult result = TexconvProcess.Run(
                    texconvPath,
                    outputDirectory,
                    batch.Select(item => item.Item.Source.FullName).ToArray(),
                    new TexconvOptions(
                        "BC7_UNORM",
                        batch[0].Item.MipCount,
                        singleProcess: false,
                        gpuAdapter: 0),
                    cancellationToken);
                if (result.ExitCode != 0)
                {
                    string detail = !string.IsNullOrWhiteSpace(result.Error)
                        ? result.Error
                        : result.Output;
                    throw new InvalidOperationException(
                        "texconv failed with exit code " +
                        result.ExitCode.ToString(CultureInfo.InvariantCulture) +
                        ": " + detail.Trim());
                }

                foreach (DdsConversion conversion in batch)
                {
                    string outputPath = Path.Combine(
                        outputDirectory,
                        conversion.OutputName + ".DDS");
                    if (!File.Exists(outputPath))
                    {
                        throw new FileNotFoundException(
                            "texconv did not create the expected DDS.",
                            outputPath);
                    }

                    outputs.Add(conversion.Item, outputPath);
                }
            }

            return outputs;
        }

        private int MaintainStore()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref maintenanceRequested, 0);
            int removed = store.SweepOrphans();
            long reserve = Interlocked.Read(ref minimumFreeBytes);
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(cacheRoot));
            long effective = DdsBudget.EffectiveMaximum(Interlocked.Read(ref maximumCacheBytes),
                store.CurrentBytes, drive.AvailableFreeSpace, reserve);
            AddInvalidated(store.EnforceBudget(effective));
            long free = drive.AvailableFreeSpace;
            SetCacheBytes(store.CurrentBytes);
            Interlocked.Exchange(ref availableFreeBytes, free);
            Interlocked.Exchange(ref effectiveBudgetBytes, DdsBudget.EffectiveMaximum(
                Interlocked.Read(ref maximumCacheBytes), store.CurrentBytes, free, reserve));
            Volatile.Write(ref reserveWarning, free < reserve
                ? "Drive space is below the free-space reserve. DDS cannot reclaim enough space from its cache alone."
                : null);
            return removed;
        }

        private void PostCompletion(DdsBuildResult result) => completions.Enqueue(result);

        private void CompleteBuild(DdsBuildResult result)
        {
            Interlocked.Add(ref created, result.Created);
            Interlocked.Add(ref failed, result.Failed);
            Interlocked.Add(ref buildMilliseconds, (long)Math.Round(result.Milliseconds));
            buildProfile.Observe(TimeSpan.FromMilliseconds(result.Milliseconds), result.Error == null);
            completedBuilds++;
            if (result.Error != null)
            {
                lastError = result.Error;
                Log.Warning("[FixWorld] DDS pack build failed: " + result.Error);
            }
            if (completedBuilds >= scheduledBuilds)
                Log.Message("[FixWorld] DDS background build finished: " + created + " created, " + failed + " failed.");
        }

        private TextureDdsCacheSnapshot GetSnapshotCore(bool enabled)
        {
            return new TextureDdsCacheSnapshot(
                enabled && cacheRoot != null,
                Interlocked.Read(ref hits),
                Interlocked.Read(ref misses),
                Interlocked.Read(ref created),
                Interlocked.Read(ref invalidated),
                Interlocked.Read(ref excluded),
                Interlocked.Read(ref unsupported),
                Interlocked.Read(ref budgetSkipped),
                Interlocked.Read(ref failed),
                Interlocked.Read(ref buildMilliseconds),
                Interlocked.Read(ref cacheBytes),
                Interlocked.Read(ref maximumCacheBytes),
                workerCount,
                Interlocked.Read(ref workerPreparedMods),
                Interlocked.Read(ref workerAppliedMods),
                Interlocked.Read(ref workerFallbackMods), Busy, failedBuildPlans.Count, lastError, cacheRoot,
                Interlocked.Read(ref minimumFreeBytes), Interlocked.Read(ref availableFreeBytes),
                Interlocked.Read(ref effectiveBudgetBytes), Volatile.Read(ref reserveWarning),
                maximumOverridden, minimumFreeOverridden, Volatile.Read(ref maintenanceRequested) != 0,
                plannedMods, processedMods, currentMod);
        }

        private bool Configure()
        {

            if (string.Equals(
                    Environment.GetEnvironmentVariable(
                        DdsCacheContract.EnabledEnvironmentVariable),
                    "0",
                    StringComparison.Ordinal))
            {
                return false;
            }

            cacheRoot = Environment.GetEnvironmentVariable(
                DdsCacheContract.CacheRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(cacheRoot))
            {
                cacheRoot = Path.Combine(
                    GenFilePaths.SaveDataFolderPath,
                    "FixWorld",
                    "TextureCache",
                    DdsCacheContract.CacheDirectoryName);
            }

            cacheRoot = Path.GetFullPath(cacheRoot);
            Directory.CreateDirectory(cacheRoot);

            maximumCacheBytes = DdsSettings.ReadOverride(DdsSettings.MaximumEnvironmentVariable,
                Settings?.MaximumGiB.Value ?? DdsSettings.DefaultMaximumGiB);
            minimumFreeBytes = DdsSettings.ReadOverride(DdsSettings.MinimumFreeEnvironmentVariable,
                Settings?.MinimumFreeGiB.Value ?? DdsSettings.DefaultMinimumFreeGiB);
            maximumOverridden = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DdsSettings.MaximumEnvironmentVariable));
            minimumFreeOverridden = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DdsSettings.MinimumFreeEnvironmentVariable));
            return true;
        }

        private void LogConfiguration()
        {
            string maximum = ToGiB(maximumCacheBytes)
                .ToString("0.###", CultureInfo.InvariantCulture) + " GiB";
            if (!string.IsNullOrEmpty(texconvPath))
            {
                Log.Message(
                    "[FixWorld] DDS pack cache enabled at " + cacheRoot +
                    "; index=" + store.LoadStatus +
                    "; entries=" + store.EntryCount +
                    "; texconv=" + texconvPath +
                    "; workers=" + workerCount +
                    "; maxCache=" + maximum + ".");
            }
            else
            {
                Log.Warning(
                    "[FixWorld] DDS packs can be read, but texconv was not " +
                    "found. Missing textures use their source files.");
            }
        }

        private static long Align(long bytes)
        {
            return (bytes + 4095L) & ~4095L;
        }

        private static void Align(Stream stream)
        {
            long padding = Align(stream.Position) - stream.Position;
            if (padding == 0L)
            {
                return;
            }

            byte[] zeros = new byte[Math.Min(4096, (int)padding)];
            while (padding > 0L)
            {
                int count = (int)Math.Min(zeros.Length, padding);
                stream.Write(zeros, 0, count);
                padding -= count;
            }
        }

        private static double ElapsedMilliseconds(long startedAt)
        {
            return (Stopwatch.GetTimestamp() - startedAt) * 1000.0 /
                   Stopwatch.Frequency;
        }

        private void AddInvalidated(long count)
        {
            Interlocked.Add(ref invalidated, count);
        }

        private void SetCacheBytes(long bytes)
        {
            Interlocked.Exchange(ref cacheBytes, bytes);
        }

        private static double ToGiB(long bytes)
        {
            return bytes / (1024.0 * 1024.0 * 1024.0);
        }

    }

    internal sealed class DdsModPlan
    {
        internal DdsModPlan(
            string packageId,
            IReadOnlyDictionary<string, DdsPackSlice> hits,
            IReadOnlyList<DdsPackItem> items,
            int missingCount,
            int excluded,
            int unsupported,
            long estimatedPackBytes)
        {
            PackageId = packageId;
            Hits = hits.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            Items = items.ToArray();
            MissingCount = missingCount;
            Excluded = excluded;
            Unsupported = unsupported;
            EstimatedPackBytes = estimatedPackBytes;
            Generation = DdsCacheKey.HashText(
                packageId + "\n" + string.Join(
                    "\n",
                    Items.Select(item =>
                        item.SourcePath + "|" +
                        item.Source.Length.ToString(
                            CultureInfo.InvariantCulture) + "|" +
                        item.Source.LastWriteTimeUtc.Ticks.ToString(
                            CultureInfo.InvariantCulture))));
        }

        internal string PackageId { get; }
        internal IReadOnlyDictionary<string, DdsPackSlice> Hits { get; }
        internal IReadOnlyList<DdsPackItem> Items { get; }
        internal int MissingCount { get; }
        internal int Excluded { get; }
        internal int Unsupported { get; }
        internal long EstimatedPackBytes { get; }
        internal string Generation { get; }
    }

    internal sealed class DdsPackItem
    {
        private DdsPackItem(
            string logicalPath,
            string sourcePath,
            FileInfo source,
            string sourceHash,
            int mipCount,
            long estimatedBytes,
            DdsPackSlice existing,
            bool hasExisting)
        {
            LogicalPath = logicalPath;
            SourcePath = sourcePath;
            Source = source;
            SourceHash = sourceHash;
            MipCount = mipCount;
            EstimatedBytes = estimatedBytes;
            Existing = existing;
            HasExisting = hasExisting;
        }

        internal string LogicalPath { get; }
        internal string SourcePath { get; }
        internal FileInfo Source { get; }
        internal string SourceHash { get; }
        internal int MipCount { get; }
        internal long EstimatedBytes { get; }
        internal DdsPackSlice Existing { get; }
        internal bool HasExisting { get; }

        internal static DdsPackItem FromExisting(
            string logicalPath,
            string sourcePath,
            FileInfo source,
            string sourceHash,
            DdsPackSlice existing)
        {
            return new DdsPackItem(
                logicalPath,
                sourcePath,
                source,
                sourceHash,
                0,
                existing.Length,
                existing,
                true);
        }

        internal static DdsPackItem FromMissing(
            string logicalPath,
            string sourcePath,
            FileInfo source,
            string sourceHash,
            int mipCount,
            long estimatedBytes)
        {
            return new DdsPackItem(
                logicalPath,
                sourcePath,
                source,
                sourceHash,
                mipCount,
                estimatedBytes,
                default,
                false);
        }
    }

    internal readonly struct DdsConversion
    {
        internal DdsConversion(
            DdsPackItem item,
            string outputName,
            string group)
        {
            Item = item;
            OutputName = outputName;
            Group = group;
        }

        internal DdsPackItem Item { get; }
        internal string OutputName { get; }
        internal string Group { get; }
    }

    internal readonly struct DdsBuildResult
    {
        internal DdsBuildResult(
            int created,
            int failed,
            double milliseconds,
            string error)
        {
            Created = created;
            Failed = failed;
            Milliseconds = milliseconds;
            Error = error;
        }

        internal int Created { get; }
        internal int Failed { get; }
        internal double Milliseconds { get; }
        internal string Error { get; }
    }



    internal static class DdsPackTextureLoader
    {
        internal unsafe static Texture2D Load(
            MemoryMappedFileSpanWrapper reader,
            DdsPackSlice slice,
            string name)
        {
            if (slice.Length < DdsPayload.HeaderBytes || slice.Length > int.MaxValue ||
                slice.Offset < 0 || slice.Length > reader.FileSize || slice.Offset > reader.FileSize - slice.Length)
            {
                throw new InvalidDataException(
                    "A packed DDS entry exceeds the supported size.");
            }

            DdsHeader header = reader.Read<DdsHeader>(slice.Offset);
            if (header.Magic != DdsHeader.RequiredMagic ||
                header.Size != DdsHeader.RequiredSize ||
                header.PixelFormat.Size != DdsPixelFormat.RequiredSize || !header.PixelFormat.IsBc7 ||
                header.Caps2 != 0 || header.Depth > 1 ||
                reader.Read<uint>(slice.Offset + 128) != 98 || // DXGI_FORMAT_BC7_UNORM
                reader.Read<uint>(slice.Offset + 132) != 3 || // TEXTURE2D
                reader.Read<uint>(slice.Offset + 136) != 0 ||
                reader.Read<uint>(slice.Offset + 140) != 1)
            {
                throw new InvalidDataException(
                    "The packed DDS header is invalid.");
            }

            bool hasMipmaps =
                (header.Flags & DdsHeaderFlags.MipMapCount) != 0 &&
                header.MipMapCount > 1;
            int mipCount = hasMipmaps ? (int)header.MipMapCount : 1;
            int payloadLength = DdsPayload.Validate(reader.FileSize, slice.Offset, slice.Length,
                header.Width, header.Height, (uint)mipCount);
            Texture2D texture = new Texture2D(
                (int)header.Width,
                (int)header.Height,
                TextureFormat.BC7,
                mipCount,
                linear: false,
                createUninitialized: true);
            try
            {
                byte* pointer = reader.GetDirectPointer() + slice.Offset + DdsPayload.HeaderBytes;
                texture.LoadRawTextureData((IntPtr)pointer, payloadLength);
                texture.name = Path.GetFileNameWithoutExtension(name);
                texture.filterMode = FilterMode.Trilinear;
                texture.anisoLevel = 2;
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                return texture;
            }
            catch { UnityEngine.Object.Destroy(texture); throw; }
        }
    }

}
