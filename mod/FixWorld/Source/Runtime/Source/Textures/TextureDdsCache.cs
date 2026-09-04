using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using FixWorld.ExternalTools;
using FixWorld.Migrations;
using FixWorld.Runtime;
using FixWorld.Scheduling;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.Textures
{
    internal sealed class TextureDdsCache
    {
        private const string BackgroundOutputEnvironmentVariable =
            "FIXWORLD_DDS_BACKGROUND_OUTPUT";
        private const string MaxCacheGiBEnvironmentVariable =
            "FIXWORLD_DDS_CACHE_MAX_GIB";
        private const string MinimumFreeGiBEnvironmentVariable =
            "FIXWORLD_DDS_CACHE_MIN_FREE_GIB";
        private const string WorkerCountEnvironmentVariable =
            "FIXWORLD_DDS_WORKERS";
        private const long DefaultMinimumFreeBytes =
            10L * 1024L * 1024L * 1024L;

        private readonly object sync = new object();
        private readonly JobScheduler scheduler;
        private readonly MainThreadQueue mainThread;
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
        private readonly Dictionary<string, DdsModPlan> failedBuildPlans =
            new Dictionary<string, DdsModPlan>(StringComparer.Ordinal);
        private readonly Dictionary<string, DdsPackSlice> startupHits =
            new Dictionary<string, DdsPackSlice>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MemoryMappedFileSpanWrapper>
            startupReaders =
                new Dictionary<string, MemoryMappedFileSpanWrapper>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly List<JobHandle> jobs = new List<JobHandle>();
        private DdsModPlan[] pendingBuildPlans = Array.Empty<DdsModPlan>();
        private DdsPackSnapshot discoverySnapshot;

        private string cacheRoot;
        private string legacyCacheRoot;
        private string texconvPath;
        private string converterIdentity;
        private long maximumCacheBytes;
        private long minimumFreeBytes;
        private int workerCount;
        private DdsPackStore store;
        private bool attached;
        private bool backgroundStarted;
        private bool observingTextureDiscovery;
        private bool stopped;
        private int scheduledBuilds;
        private int completedBuilds;

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
        private TextureDdsCacheSnapshot lastSnapshot =
            TextureDdsCacheSnapshot.Disabled(workerCount: 0);

        internal TextureDdsCache(
            JobScheduler scheduler,
            MainThreadQueue mainThread)
        {
            this.scheduler = scheduler ??
                throw new ArgumentNullException(nameof(scheduler));
            this.mainThread = mainThread ??
                throw new ArgumentNullException(nameof(mainThread));
        }

        internal void Attach(string modRoot, float ddsCacheMaxGiB)
        {
            lock (sync)
            {
                if (attached)
                {
                    maximumCacheBytes = ReadGiBLimit(
                        MaxCacheGiBEnvironmentVariable,
                        GiBToBytes(ddsCacheMaxGiB));
                    if (store != null)
                    {
                        AddInvalidated(store.EnforceBudget(maximumCacheBytes));
                        SetCacheBytes(store.CurrentBytes);
                    }

                    return;
                }

                attached = true;
                if (!Configure(ddsCacheMaxGiB))
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
                    lastSnapshot = GetSnapshotCore(enabled: true);
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

        internal void BeginIndex()
        {
            lock (sync)
            {
                ResetDiscovery();
                startupHits.Clear();
                DisposeStartupReaders();
                pendingBuildPlans = Array.Empty<DdsModPlan>();
                backgroundStarted = false;
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

                DdsCacheContract.RequestReadAheadStop();
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
                if (pendingBuildPlans.Length == 0)
                {
                    PostCompletion(new DdsBuildResult(0, 0, 0.0, null));
                }

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
                    if (!startupReaders.TryGetValue(
                            slice.Path,
                            out MemoryMappedFileSpanWrapper reader))
                    {
                        reader = new MemoryMappedFileSpanWrapper(slice.Path);
                        startupReaders.Add(slice.Path, reader);
                    }

                    texture = DdsPackTextureLoader.Load(
                        reader,
                        slice,
                        source.Name);
                    return true;
                }
                catch (Exception exception)
                {
                    startupHits.Remove(source.FullPath);
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

                Interlocked.Add(ref hits, plan.Hits.Count);
                Interlocked.Add(ref misses, plan.MissingCount);
                Interlocked.Add(ref excluded, plan.Excluded);
                Interlocked.Add(ref unsupported, plan.Unsupported);

                retainedByPackage[plan.PackageId] = new HashSet<string>(
                    plan.Items.Select(item => item.SourcePath),
                    StringComparer.Ordinal);
                if (plan.MissingCount == 0)
                {
                    return;
                }

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
            }
        }

        internal TextureDdsCacheSnapshot GetSnapshot()
        {
            lock (sync)
            {
                return IsRunning
                    ? GetSnapshotCore(enabled: true)
                    : lastSnapshot;
            }
        }

        internal string ClearCache()
        {
            lock (sync)
            {
                if (!IsRunning)
                {
                    return "The DDS cache is not active.";
                }

                if (jobs.Any(job => !job.IsTerminal))
                {
                    return "DDS work is still running. Try again when it has finished.";
                }

                DisposeStartupReaders();
                startupHits.Clear();
                pendingBuildPlans = Array.Empty<DdsModPlan>();
                failedBuildPlans.Clear();
                jobs.Clear();
                ResetDiscovery();

                DdsPackStore currentStore = store;
                store = null;
                currentStore.Dispose();

                MigrationCleanupResult result;
                try
                {
                    result = MigrationCleanup.DeleteDirectory(
                        cacheRoot,
                        DdsCacheContract.CacheDirectoryName);
                }
                finally
                {
                    store = DdsPackStore.Open(
                        cacheRoot,
                        DdsCacheContract.CacheIdentityVersion);
                    SetCacheBytes(store.CurrentBytes);
                }

                if (result.Errors.Count > 0)
                {
                    return "The DDS cache could not be fully cleared: " +
                           string.Join("; ", result.Errors);
                }

                double mebibytes = result.Bytes / (1024.0 * 1024.0);
                return "DDS cache cleared (" +
                       mebibytes.ToString("N1", CultureInfo.InvariantCulture) +
                       " MiB). Restart RimWorld to rebuild it.";
            }
        }

        internal string RetryFailedBuilds()
        {
            lock (sync)
            {
                if (!IsRunning)
                {
                    return "The DDS cache is not active.";
                }

                if (jobs.Any(job => !job.IsTerminal))
                {
                    return "DDS background work is already running.";
                }

                if (failedBuildPlans.Count == 0)
                {
                    return "There are no failed DDS builds to retry.";
                }

                DdsModPlan[] retryPlans = failedBuildPlans.Values.ToArray();
                failedBuildPlans.Clear();
                jobs.Clear();
                ScheduleBuilds(retryPlans);
                Log.Message(
                    "[FixWorld] Retrying failed DDS pack builds; queuedMods=" +
                    retryPlans.Length.ToString(CultureInfo.InvariantCulture) + ".");
                return "Retry started for " +
                       retryPlans.Length.ToString(CultureInfo.InvariantCulture) +
                       (retryPlans.Length == 1 ? " mod." : " mods.");
            }
        }

        internal void Shutdown()
        {
            JobHandle[] scheduled;
            DdsPackStore currentStore;
            lock (sync)
            {
                if (stopped)
                {
                    return;
                }

                stopped = true;
                lastSnapshot = GetSnapshotCore(enabled: false);
                scheduled = jobs.ToArray();
                currentStore = store;
                store = null;
                ResetDiscovery();
                startupHits.Clear();
                DisposeStartupReaders();
                pendingBuildPlans = Array.Empty<DdsModPlan>();
                failedBuildPlans.Clear();
            }

            foreach (JobHandle job in scheduled)
            {
                scheduler.Cancel(job);
            }

            using (CancellationTokenSource timeout =
                   new CancellationTokenSource(2000))
            {
                try
                {
                    foreach (JobHandle job in scheduled)
                    {
                        if (!job.IsTerminal)
                        {
                            job.Wait(timeout.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Warning(
                        "[FixWorld] DDS pack jobs did not stop within two " +
                        "seconds.");
                }
            }

            if (currentStore != null)
            {
                currentStore.Save();
                currentStore.Dispose();
            }
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
            pendingBuildPlans = discoveredBuildPlans.ToArray();
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
            foreach (MemoryMappedFileSpanWrapper reader in startupReaders.Values)
            {
                reader.Dispose();
            }

            startupReaders.Clear();
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

        private void ScheduleBuilds(IReadOnlyList<DdsModPlan> buildPlans)
        {
            scheduledBuilds = buildPlans.Count;
            completedBuilds = 0;
            List<JobHandle> buildHandles = new List<JobHandle>(
                buildPlans.Count);
            foreach (DdsModPlan plan in buildPlans)
            {
                JobHandle<DdsBuildResult> handle = scheduler.Schedule(
                    new Job<DdsBuildResult>(
                        "dds-pack/" + plan.PackageId + "/" + plan.Generation,
                        token => BuildAndPublish(plan, token),
                        name: "Build DDS pack for " + plan.PackageId,
                        lifetime: JobLifetime.Background,
                        priority: JobPriority.Low,
                        resourceClass: JobResourceClass.Mixed,
                        estimatedBytes: plan.EstimatedPackBytes * 2L,
                        concurrencyKey: "dds-pack-build",
                        maxConcurrency: 1));
                jobs.Add(handle);
                buildHandles.Add(handle);
            }

            JobHandle<int> maintenance = scheduler.Schedule(
                new Job<int>(
                    "dds-pack-maintenance/" + Guid.NewGuid().ToString("N"),
                    _ => MaintainStore(),
                    name: "Clean obsolete DDS packs",
                    lifetime: JobLifetime.Background,
                    priority: JobPriority.Low,
                    resourceClass: JobResourceClass.Io,
                    dependencies: buildHandles,
                    concurrencyKey: "dds-pack-store",
                    maxConcurrency: 1));
            jobs.Add(maintenance);

            if (!string.IsNullOrEmpty(legacyCacheRoot) &&
                Directory.Exists(legacyCacheRoot))
            {
                JobHandle<MigrationCleanupResult> migration =
                    scheduler.Schedule(
                        new Job<MigrationCleanupResult>(
                            "migration/dds-v1-cleanup",
                            CleanLegacyCache,
                            name: "Remove legacy DDS cache",
                            lifetime: JobLifetime.Background,
                            priority: JobPriority.Low,
                            resourceClass: JobResourceClass.Io,
                            dependencies: buildHandles,
                            concurrencyKey: "dds-pack-store",
                            maxConcurrency: 1));
                jobs.Add(migration);
            }
        }

        private MigrationCleanupResult CleanLegacyCache(
            CancellationToken cancellationToken)
        {
            MigrationCleanupResult result =
                LegacyDdsCacheMigration.Clean(
                    legacyCacheRoot,
                    cancellationToken);
            try
            {
                mainThread.Post(
                    "Report legacy DDS cache migration",
                    () => ReportLegacyCacheMigration(result));
            }
            catch (ObjectDisposedException)
            {
            }

            return result;
        }

        private static void ReportLegacyCacheMigration(
            MigrationCleanupResult result)
        {
            if (result.Removed)
            {
                Log.Message(
                    "[FixWorld] Removed " + result.Files +
                    " files from the legacy DDS cache (" +
                    ToMiB(result.Bytes).ToString(
                        "0.0",
                        CultureInfo.InvariantCulture) + " MiB).");
            }

            if (result.Errors.Count > 0)
            {
                Log.Warning(
                    "[FixWorld] Legacy DDS cleanup could not remove the " +
                    "complete cache: " +
                    string.Join("; ", result.Errors));
            }
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
                if (plan.EstimatedPackBytes > maximumCacheBytes ||
                    availableBytes - temporaryBytes < minimumFreeBytes)
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
                lock (sync)
                {
                    if (!IsRunning)
                    {
                        throw new OperationCanceledException();
                    }

                    store.Publish(pack);
                    store.EnforceBudget(maximumCacheBytes);
                    SetCacheBytes(store.CurrentBytes);
                    failedBuildPlans.Remove(plan.PackageId);
                }

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
                lock (sync)
                {
                    store?.Discard(pack);
                    if (pack == null)
                    {
                        store?.DiscardStaging(stagingRoot);
                    }

                }

                throw;
            }
            catch (Exception exception)
            {
                lock (sync)
                {
                    store?.Discard(pack);
                    if (pack == null)
                    {
                        store?.DiscardStaging(stagingRoot);
                    }

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
                if (item.HasExisting)
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
            lock (sync)
            {
                stagingRoot = store.CreateStagingRoot(plan.PackageId);
            }

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
                    plan.Generation,
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
            lock (sync)
            {
                if (!IsRunning)
                {
                    return 0;
                }

                int removed = store.SweepOrphans();
                SetCacheBytes(store.CurrentBytes);
                return removed;
            }
        }

        private void PostCompletion(DdsBuildResult result)
        {
            try
            {
                mainThread.Post(
                    "Complete DDS pack build",
                    () => CompleteBuild(result));
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void CompleteBuild(DdsBuildResult result)
        {
            Interlocked.Add(ref created, result.Created);
            Interlocked.Add(ref failed, result.Failed);
            Interlocked.Add(
                ref buildMilliseconds,
                (long)Math.Round(result.Milliseconds));
            completedBuilds++;
            if (result.Error != null)
            {
                Log.Warning(
                    "[FixWorld] DDS pack build failed: " + result.Error);
            }

            if (completedBuilds < scheduledBuilds)
            {
                return;
            }

            TextureCacheBuildReport report = new TextureCacheBuildReport(
                (int)Interlocked.Read(ref created),
                (int)Interlocked.Read(ref failed),
                Interlocked.Read(ref buildMilliseconds),
                1);
            WriteBackgroundReport(report);
            Log.Message(
                "[FixWorld] DDS pack build complete: " +
                report.Created.ToString(CultureInfo.InvariantCulture) +
                " created, " +
                report.Failed.ToString(CultureInfo.InvariantCulture) +
                " failed in " +
                (report.Milliseconds / 1000.0).ToString(
                    "F1",
                    CultureInfo.InvariantCulture) + " s.");
        }

        private void WriteBackgroundReport(TextureCacheBuildReport report)
        {
            string path = Environment.GetEnvironmentVariable(
                BackgroundOutputEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(
                        typeof(TextureCacheBuildReport));
                AtomicFile.Write(
                    path,
                    stream => serializer.WriteObject(stream, report));
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[FixWorld] Could not write DDS background report: " +
                    exception);
            }
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
                maximumCacheBytes,
                workerCount,
                Interlocked.Read(ref workerPreparedMods),
                Interlocked.Read(ref workerAppliedMods),
                Interlocked.Read(ref workerFallbackMods));
        }

        private bool Configure(float ddsCacheMaxGiB)
        {
            workerCount = ReadWorkerCount();
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
            legacyCacheRoot = LegacyDdsCacheMigration.GetRoot(cacheRoot);
            maximumCacheBytes = ReadGiBLimit(
                MaxCacheGiBEnvironmentVariable,
                GiBToBytes(ddsCacheMaxGiB));
            minimumFreeBytes = ReadGiBLimit(
                MinimumFreeGiBEnvironmentVariable,
                DefaultMinimumFreeBytes);
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

        private static double ToMiB(long bytes)
        {
            return bytes / (1024.0 * 1024.0);
        }

        private static int ReadWorkerCount()
        {
            string value = Environment.GetEnvironmentVariable(
                WorkerCountEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return Math.Min(
                    32,
                    Math.Max(1, Environment.ProcessorCount / 2));
            }

            if (!int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int count) ||
                count < 0 || count > 32)
            {
                throw new InvalidOperationException(
                    "Invalid worker count in " +
                    WorkerCountEnvironmentVariable + ": " + value);
            }

            return count;
        }

        private static long ReadGiBLimit(
            string environmentVariable,
            long defaultBytes)
        {
            string value = Environment.GetEnvironmentVariable(
                environmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultBytes;
            }

            if (!double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double gibibytes) ||
                gibibytes <= 0.0)
            {
                throw new InvalidOperationException(
                    "Invalid positive GiB value in " +
                    environmentVariable + ": " + value);
            }

            return GiBToBytes(gibibytes);
        }

        private static long GiBToBytes(double gibibytes)
        {
            double bytes = gibibytes * 1024.0 * 1024.0 * 1024.0;
            if (gibibytes <= 0.0 || bytes > long.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(gibibytes));
            }

            return (long)Math.Floor(bytes);
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

    [DataContract]
    internal sealed class TextureCacheBuildReport
    {
        internal TextureCacheBuildReport(
            int created,
            int failed,
            long milliseconds,
            int workers)
        {
            Created = created;
            Failed = failed;
            Milliseconds = milliseconds;
            Workers = workers;
        }

        [DataMember(Name = "created", Order = 1)]
        public int Created { get; private set; }

        [DataMember(Name = "failed", Order = 2)]
        public int Failed { get; private set; }

        [DataMember(Name = "milliseconds", Order = 3)]
        public long Milliseconds { get; private set; }

        [DataMember(Name = "workers", Order = 4)]
        public int Workers { get; private set; }
    }

    internal static class DdsPackTextureLoader
    {
        internal unsafe static Texture2D Load(
            MemoryMappedFileSpanWrapper reader,
            DdsPackSlice slice,
            string name)
        {
            if (slice.Length > int.MaxValue)
            {
                throw new InvalidDataException(
                    "A packed DDS entry exceeds the supported size.");
            }

            DdsHeader header = reader.Read<DdsHeader>(slice.Offset);
            if (header.Magic != DdsHeader.RequiredMagic ||
                header.Size != DdsHeader.RequiredSize ||
                header.PixelFormat.Size != DdsPixelFormat.RequiredSize)
            {
                throw new InvalidDataException(
                    "The packed DDS header is invalid.");
            }

            TextureFormat format = header.PixelFormat.ToTextureFormat();
            int headerBytes = header.PixelFormat.IsBc7 ? 148 : 128;
            long payloadLength = slice.Length - headerBytes;
            if (payloadLength <= 0L || payloadLength > int.MaxValue)
            {
                throw new InvalidDataException(
                    "The packed DDS payload is invalid.");
            }

            bool hasMipmaps =
                (header.Flags & DdsHeaderFlags.MipMapCount) != 0 &&
                header.MipMapCount > 1;
            int mipCount = hasMipmaps ? (int)header.MipMapCount : 1;
            Texture2D texture = new Texture2D(
                (int)header.Width,
                (int)header.Height,
                format,
                mipCount,
                linear: false,
                createUninitialized: true);
            byte* pointer = reader.GetDirectPointer() +
                            slice.Offset + headerBytes;
            texture.LoadRawTextureData(
                (IntPtr)pointer,
                (int)payloadLength);

            texture.name = Path.GetFileNameWithoutExtension(name);
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 2;
            texture.Apply(!hasMipmaps, makeNoLongerReadable: true);
            return texture;
        }
    }

    [DataContract]
    internal sealed class TextureDdsCacheSnapshot
    {
        internal TextureDdsCacheSnapshot(
            bool enabled,
            long hits,
            long misses,
            long created,
            long invalidated,
            long excluded,
            long unsupported,
            long budgetSkipped,
            long failed,
            long buildMilliseconds,
            long cacheBytes,
            long maxCacheBytes,
            int workerCount,
            long workerPreparedMods,
            long workerAppliedMods,
            long workerFallbackMods)
        {
            Enabled = enabled;
            Hits = hits;
            Misses = misses;
            Created = created;
            Invalidated = invalidated;
            Excluded = excluded;
            Unsupported = unsupported;
            BudgetSkipped = budgetSkipped;
            Failed = failed;
            BuildMilliseconds = buildMilliseconds;
            CacheBytes = cacheBytes;
            MaxCacheBytes = maxCacheBytes;
            WorkerCount = workerCount;
            WorkerPreparedMods = workerPreparedMods;
            WorkerAppliedMods = workerAppliedMods;
            WorkerFallbackMods = workerFallbackMods;
        }

        [DataMember(Name = "enabled", Order = 1)]
        public bool Enabled { get; private set; }
        [DataMember(Name = "hits", Order = 2)]
        public long Hits { get; private set; }
        [DataMember(Name = "misses", Order = 3)]
        public long Misses { get; private set; }
        [DataMember(Name = "created", Order = 4)]
        public long Created { get; private set; }
        [DataMember(Name = "invalidated", Order = 5)]
        public long Invalidated { get; private set; }
        [DataMember(Name = "excluded", Order = 6)]
        public long Excluded { get; private set; }
        [DataMember(Name = "unsupported", Order = 7)]
        public long Unsupported { get; private set; }
        [DataMember(Name = "budgetSkipped", Order = 8)]
        public long BudgetSkipped { get; private set; }
        [DataMember(Name = "failed", Order = 9)]
        public long Failed { get; private set; }
        [DataMember(Name = "buildMs", Order = 10)]
        public long BuildMilliseconds { get; private set; }
        [DataMember(Name = "cacheBytes", Order = 11)]
        public long CacheBytes { get; private set; }
        [DataMember(Name = "maxCacheBytes", Order = 12)]
        public long MaxCacheBytes { get; private set; }
        [DataMember(Name = "workerCount", Order = 13)]
        public int WorkerCount { get; private set; }
        [DataMember(Name = "workerPreparedMods", Order = 14)]
        public long WorkerPreparedMods { get; private set; }
        [DataMember(Name = "workerAppliedMods", Order = 15)]
        public long WorkerAppliedMods { get; private set; }
        [DataMember(Name = "workerFallbackMods", Order = 16)]
        public long WorkerFallbackMods { get; private set; }

        internal static TextureDdsCacheSnapshot Disabled(int workerCount)
        {
            return new TextureDdsCacheSnapshot(
                false,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                workerCount,
                0L,
                0L,
                0L);
        }
    }
}
