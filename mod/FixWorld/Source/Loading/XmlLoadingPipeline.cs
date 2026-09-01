using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using FixWorld.Scheduling;
using Verse;

namespace FixWorld.Loading
{
    internal static class XmlLoadingPipeline
    {
        private const string WorkerEnvironmentVariable = "FIXWORLD_XML_WORKERS";
        private const string VerificationEnvironmentVariable =
            "FIXWORLD_XML_VERIFY_ORIGINAL";
        private const string DiscoveryConcurrencyKey = "loader/xml/discovery";
        private const string ParseConcurrencyKey = "loader/xml/parse";
        private const int ParseBatchSize = 64;
        private static readonly object SnapshotSync = new object();

        private static long nextRunId;
        private static XmlLoadingSnapshot snapshot = XmlLoadingSnapshot.Empty;

        internal static List<LoadableXmlAsset> Run(bool hotReload)
        {
            long runId = Interlocked.Increment(ref nextRunId);
            long startedAt = Stopwatch.GetTimestamp();
            ModContentPack[] mods = LoadedModManager.RunningModsListForReading.ToArray();
            int workerCount = ReadWorkerCount();
            XmlModTarget[] targets = mods
                .Select((mod, index) => CreateTarget(mod, index, hotReload))
                .ToArray();
            ScheduledJobHandle<XmlModSource>[] discoveryHandles =
                new ScheduledJobHandle<XmlModSource>[targets.Length];
            List<XmlParseBatchHandle> parseBatches = new List<XmlParseBatchHandle>();
            LoadableXmlAsset[][] parsedAssets = new LoadableXmlAsset[targets.Length][];
            bool[] parseFailed = new bool[targets.Length];
            Exception[] parseErrors = new Exception[targets.Length];
            long[] parseTicks = new long[targets.Length];
            long[] parseWaitTicks = new long[targets.Length];
            XmlModLoadingSnapshot[] modSnapshots =
                new XmlModLoadingSnapshot[targets.Length];
            List<LoadableXmlAsset> result = new List<LoadableXmlAsset>();
            int fallbackMods = 0;
            int failedMods = 0;

            try
            {
                ScheduleDiscovery(runId, workerCount, targets, discoveryHandles);
                WaitFor(discoveryHandles);
                ScheduleParsing(
                    runId,
                    workerCount,
                    targets,
                    discoveryHandles,
                    parseBatches,
                    parsedAssets);
                CompleteParsing(
                    parseBatches,
                    parsedAssets,
                    parseFailed,
                    parseErrors,
                    parseTicks,
                    parseWaitTicks);

                for (int index = 0; index < targets.Length; index++)
                {
                    XmlModTarget target = targets[index];
                    ScheduledJobHandle<XmlModSource> discovery = discoveryHandles[index];
                    bool fallback = discovery.State != SchedulerJobState.Completed ||
                                    parseFailed[index];
                    LoadableXmlAsset[] assets;
                    long fallbackTicks = 0L;
                    bool failed = false;

                    if (fallback)
                    {
                        fallbackMods++;
                        Exception workerError = parseErrors[index] ?? discovery.Exception;
                        Log.Warning(
                            "[FixWorld] XML workers fell back to RimWorld for " +
                            target.PackageId +
                            (workerError == null ? "." : ": " + workerError.Message));
                        long fallbackStartedAt = Stopwatch.GetTimestamp();
                        assets = LoadOriginal(target, out failed);
                        fallbackTicks = Stopwatch.GetTimestamp() - fallbackStartedAt;
                    }
                    else
                    {
                        assets = parsedAssets[index] ?? Array.Empty<LoadableXmlAsset>();
                    }

                    if (failed)
                    {
                        failedMods++;
                    }

                    Commit(target, assets, result);
                    XmlModSource source = discovery.State == SchedulerJobState.Completed
                        ? discovery.Result
                        : null;
                    modSnapshots[index] = new XmlModLoadingSnapshot(
                        target.PackageId,
                        target.ModName,
                        source?.Files.Length ?? assets.Length,
                        source?.TotalBytes ?? 0L,
                        ToMilliseconds(discovery.ExecutionTicks),
                        ToMilliseconds(parseTicks[index] + fallbackTicks),
                        ToMilliseconds(discovery.WaitTicks + parseWaitTicks[index]),
                        fallback,
                        failed);
                }

                VerifyOriginalOrderIfRequested(targets, result);

                PublishSnapshot(new XmlLoadingSnapshot(
                    owned: true,
                    hotReload,
                    workerCount,
                    targets.Length,
                    modSnapshots.Sum(item => item.Files),
                    modSnapshots.Sum(item => item.Bytes),
                    fallbackMods,
                    failedMods,
                    ToMilliseconds(Stopwatch.GetTimestamp() - startedAt),
                    null,
                    modSnapshots));
                Log.Message(
                    "[FixWorld] Loaded " + result.Count + " XML assets from " +
                    targets.Length + " mods with " + workerCount +
                    " workers; fallbacks=" + fallbackMods + ".");
                return result;
            }
            catch
            {
                Cancel(discoveryHandles);
                Cancel(parseBatches.Select(item => item.Handle));
                throw;
            }
        }

        internal static void RecordOriginalFallback(bool hotReload, string reason)
        {
            PublishSnapshot(new XmlLoadingSnapshot(
                owned: false,
                hotReload,
                0,
                LoadedModManager.RunningModsListForReading.Count,
                0,
                0L,
                0,
                0,
                0.0,
                reason,
                Array.Empty<XmlModLoadingSnapshot>()));
        }

        internal static XmlLoadingSnapshot GetSnapshot()
        {
            lock (SnapshotSync)
            {
                return snapshot;
            }
        }

        private static void ScheduleDiscovery(
            long runId,
            int workerCount,
            IReadOnlyList<XmlModTarget> targets,
            IList<ScheduledJobHandle<XmlModSource>> handles)
        {
            for (int index = 0; index < targets.Count; index++)
            {
                XmlModTarget target = targets[index];
                handles[index] = FixWorldScheduler.Schedule(
                    new SchedulerJob<XmlModSource>(
                        "loader/xml/" + runId + "/discover/" + index,
                        "Discover XML for " + target.ModName,
                        SchedulerJobLifetime.Critical,
                        SchedulerJobPriority.High,
                        SchedulerResourceClass.Io,
                        cancellationToken => Discover(target, cancellationToken),
                        concurrencyKey: DiscoveryConcurrencyKey,
                        maxConcurrency: workerCount));
            }
        }

        private static void ScheduleParsing(
            long runId,
            int workerCount,
            IReadOnlyList<XmlModTarget> targets,
            IReadOnlyList<ScheduledJobHandle<XmlModSource>> discoveryHandles,
            ICollection<XmlParseBatchHandle> parseBatches,
            IList<LoadableXmlAsset[]> parsedAssets)
        {
            for (int modIndex = 0; modIndex < targets.Count; modIndex++)
            {
                ScheduledJobHandle<XmlModSource> discovery = discoveryHandles[modIndex];
                if (discovery.State != SchedulerJobState.Completed)
                {
                    continue;
                }

                XmlModTarget target = targets[modIndex];
                XmlModSource source = discovery.Result;
                parsedAssets[modIndex] = new LoadableXmlAsset[source.Files.Length];
                int batchIndex = 0;
                for (int start = 0; start < source.Files.Length; start += ParseBatchSize)
                {
                    int count = Math.Min(ParseBatchSize, source.Files.Length - start);
                    int scheduledStart = start;
                    int scheduledCount = count;
                    long estimatedBytes = GetBytes(source.Files, start, count);
                    ScheduledJobHandle<XmlParseBatch> handle =
                        FixWorldScheduler.Schedule(
                    new SchedulerJob<XmlParseBatch>(
                        "loader/xml/" + runId + "/parse/" + modIndex + "/" + batchIndex,
                        "Parse XML for " + target.ModName + " batch " + (batchIndex + 1),
                        SchedulerJobLifetime.Critical,
                        SchedulerJobPriority.High,
                        SchedulerResourceClass.Cpu,
                        cancellationToken => Parse(
                            target,
                            source,
                            scheduledStart,
                            scheduledCount,
                            cancellationToken),
                        estimatedBytes: estimatedBytes,
                        concurrencyKey: ParseConcurrencyKey,
                        maxConcurrency: workerCount));
                    parseBatches.Add(new XmlParseBatchHandle(modIndex, handle));
                    batchIndex++;
                }
            }
        }

        private static void CompleteParsing(
            IEnumerable<XmlParseBatchHandle> parseBatches,
            IList<LoadableXmlAsset[]> parsedAssets,
            IList<bool> parseFailed,
            IList<Exception> parseErrors,
            IList<long> parseTicks,
            IList<long> parseWaitTicks)
        {
            foreach (XmlParseBatchHandle batch in parseBatches)
            {
                batch.Handle.Wait();
                int modIndex = batch.ModIndex;
                parseTicks[modIndex] += batch.Handle.ExecutionTicks;
                parseWaitTicks[modIndex] += batch.Handle.WaitTicks;
                if (batch.Handle.State != SchedulerJobState.Completed)
                {
                    parseFailed[modIndex] = true;
                    parseErrors[modIndex] = parseErrors[modIndex] ?? batch.Handle.Exception;
                    continue;
                }

                XmlParseBatch parsed = batch.Handle.Result;
                Array.Copy(
                    parsed.Assets,
                    0,
                    parsedAssets[modIndex],
                    parsed.Start,
                    parsed.Assets.Length);
            }
        }

        private static XmlModSource Discover(
            XmlModTarget target,
            CancellationToken cancellationToken)
        {
            LoadingOperation operation = LoadingEvents.Begin(Descriptor(
                LoadingStep.DiscoverXml,
                "Discover XML",
                "Indexing XML files for " + target.ModName,
                target));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo[] files = ModFileLoader.DiscoverXml(target.Folders);
                long totalBytes = 0L;
                for (int index = 0; index < files.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalBytes += files[index].Length;
                    operation.ReportProgress(
                        index + 1,
                        files.Length,
                        "Indexing XML for " + target.ModName);
                }

                return new XmlModSource(files, totalBytes);
            }
            catch
            {
                operation.Fail();
                throw;
            }
            finally
            {
                operation.Dispose();
            }
        }

        private static XmlParseBatch Parse(
            XmlModTarget target,
            XmlModSource source,
            int start,
            int count,
            CancellationToken cancellationToken)
        {
            LoadingOperation operation = LoadingEvents.Begin(Descriptor(
                LoadingStep.ParseXml,
                "Parse XML",
                "Parsing XML files for " + target.ModName,
                target));
            try
            {
                LoadableXmlAsset[] assets = new LoadableXmlAsset[count];
                for (int index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    assets[index] = new LoadableXmlAsset(
                        source.Files[start + index],
                        target.Mod);
                    operation.ReportProgress(
                        index + 1,
                        count,
                        "Parsing XML for " + target.ModName);
                }

                return new XmlParseBatch(start, assets);
            }
            catch
            {
                operation.Fail();
                throw;
            }
            finally
            {
                operation.Dispose();
            }
        }

        private static void Commit(
            XmlModTarget target,
            IReadOnlyCollection<LoadableXmlAsset> assets,
            ICollection<LoadableXmlAsset> result)
        {
            LoadingOperation operation = LoadingEvents.Begin(Descriptor(
                LoadingStep.CommitXml,
                "Commit XML",
                "Committing XML files for " + target.ModName,
                target));
            try
            {
                foreach (LoadableXmlAsset asset in assets)
                {
                    result.Add(asset);
                }

                operation.ReportProgress(
                    assets.Count,
                    assets.Count,
                    "Committed XML for " + target.ModName);
            }
            catch
            {
                operation.Fail();
                throw;
            }
            finally
            {
                operation.Dispose();
            }
        }

        private static LoadableXmlAsset[] LoadOriginal(
            XmlModTarget target,
            out bool failed)
        {
            failed = false;
            try
            {
                return target.Mod.LoadDefs(target.HotReload).ToArray();
            }
            catch (Exception exception)
            {
                failed = true;
                Log.Error(
                    "Could not load defs for mod " +
                    target.Mod.PackageIdPlayerFacing + ": " + exception);
                return Array.Empty<LoadableXmlAsset>();
            }
        }

        private static XmlModTarget CreateTarget(
            ModContentPack mod,
            int index,
            bool hotReload)
        {
            if (!hotReload && mod.AllDefs.Any())
            {
                Log.ErrorOnce(
                    "LoadDefs called with already existing def packages",
                    39029405);
            }

            return new XmlModTarget(
                mod,
                mod.PackageId,
                mod.Name,
                index,
                hotReload,
                mod.foldersToLoadDescendingOrder.ToArray());
        }

        private static LoadingStageEventDescriptor Descriptor(
            LoadingStep step,
            string displayName,
            string activity,
            XmlModTarget target)
        {
            return new LoadingStageEventDescriptor(
                LoadingStage.XmlAndPatches,
                step,
                displayName,
                activity,
                target.PackageId,
                LoadingModAttribution.Exact(target.PackageId, target.ModName),
                LoadingThreadAffinity.WorkerSafe);
        }

        private static void WaitFor<TResult>(
            IReadOnlyList<ScheduledJobHandle<TResult>> handles)
        {
            for (int index = 0; index < handles.Count; index++)
            {
                handles[index]?.Wait();
            }
        }

        private static void Cancel<TResult>(
            IEnumerable<ScheduledJobHandle<TResult>> handles)
        {
            foreach (ScheduledJobHandle<TResult> handle in handles)
            {
                if (handle != null && !handle.IsTerminal)
                {
                    FixWorldScheduler.Cancel(handle);
                }
            }
        }

        private static int ReadWorkerCount()
        {
            int maximum = Math.Max(1, FixWorldScheduler.WorkerCount);
            int fallback = maximum;
            string configured = Environment.GetEnvironmentVariable(
                WorkerEnvironmentVariable);
            return int.TryParse(
                       configured,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out int parsed)
                ? Math.Max(1, Math.Min(maximum, parsed))
                : fallback;
        }

        private static void VerifyOriginalOrderIfRequested(
            IReadOnlyList<XmlModTarget> targets,
            IReadOnlyList<LoadableXmlAsset> actual)
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(
                        VerificationEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }

            List<LoadableXmlAsset> expected = new List<LoadableXmlAsset>();
            for (int index = 0; index < targets.Count; index++)
            {
                expected.AddRange(DirectXmlLoader.XmlAssetsInModFolder(
                    targets[index].Mod,
                    "Defs/"));
            }

            if (expected.Count != actual.Count)
            {
                throw new InvalidOperationException(
                    "FixWorld XML order verification failed: expected " +
                    expected.Count + " assets, got " + actual.Count + ".");
            }

            for (int index = 0; index < expected.Count; index++)
            {
                if (!string.Equals(
                        expected[index].FullFilePath,
                        actual[index].FullFilePath,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "FixWorld XML order verification failed at index " + index +
                        ": expected " + expected[index].FullFilePath +
                        ", got " + actual[index].FullFilePath + ".");
                }
            }

            Log.Message(
                "[FixWorld] XML contract verified against RimWorld: " +
                actual.Count + " ordered assets.");
        }

        private static long GetBytes(
            IReadOnlyList<FileInfo> files,
            int start,
            int count)
        {
            long bytes = 0L;
            for (int index = 0; index < count; index++)
            {
                bytes += files[start + index].Length;
            }

            return bytes;
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void PublishSnapshot(XmlLoadingSnapshot value)
        {
            lock (SnapshotSync)
            {
                snapshot = value;
            }
        }

        private sealed class XmlModTarget
        {
            internal readonly ModContentPack Mod;
            internal readonly string PackageId;
            internal readonly string ModName;
            internal readonly int Index;
            internal readonly bool HotReload;
            internal readonly string[] Folders;

            internal XmlModTarget(
                ModContentPack mod,
                string packageId,
                string modName,
                int index,
                bool hotReload,
                string[] folders)
            {
                Mod = mod;
                PackageId = packageId;
                ModName = modName;
                Index = index;
                HotReload = hotReload;
                Folders = folders;
            }
        }

        private sealed class XmlModSource
        {
            internal readonly FileInfo[] Files;
            internal readonly long TotalBytes;

            internal XmlModSource(FileInfo[] files, long totalBytes)
            {
                Files = files;
                TotalBytes = totalBytes;
            }
        }

        private sealed class XmlParseBatch
        {
            internal readonly int Start;
            internal readonly LoadableXmlAsset[] Assets;

            internal XmlParseBatch(int start, LoadableXmlAsset[] assets)
            {
                Start = start;
                Assets = assets;
            }
        }

        private readonly struct XmlParseBatchHandle
        {
            internal readonly int ModIndex;
            internal readonly ScheduledJobHandle<XmlParseBatch> Handle;

            internal XmlParseBatchHandle(
                int modIndex,
                ScheduledJobHandle<XmlParseBatch> handle)
            {
                ModIndex = modIndex;
                Handle = handle;
            }
        }
    }

    internal sealed class XmlLoadingSnapshot
    {
        internal static readonly XmlLoadingSnapshot Empty = new XmlLoadingSnapshot(
            false,
            false,
            0,
            0,
            0,
            0L,
            0,
            0,
            0.0,
            "not-run",
            Array.Empty<XmlModLoadingSnapshot>());

        internal bool Owned { get; }
        internal bool HotReload { get; }
        internal int WorkerCount { get; }
        internal int Mods { get; }
        internal int Files { get; }
        internal long Bytes { get; }
        internal int FallbackMods { get; }
        internal int FailedMods { get; }
        internal double WallMilliseconds { get; }
        internal string FallbackReason { get; }
        internal IReadOnlyList<XmlModLoadingSnapshot> ModDetails { get; }

        internal XmlLoadingSnapshot(
            bool owned,
            bool hotReload,
            int workerCount,
            int mods,
            int files,
            long bytes,
            int fallbackMods,
            int failedMods,
            double wallMilliseconds,
            string fallbackReason,
            IReadOnlyList<XmlModLoadingSnapshot> modDetails)
        {
            Owned = owned;
            HotReload = hotReload;
            WorkerCount = workerCount;
            Mods = mods;
            Files = files;
            Bytes = bytes;
            FallbackMods = fallbackMods;
            FailedMods = failedMods;
            WallMilliseconds = wallMilliseconds;
            FallbackReason = fallbackReason;
            ModDetails = modDetails;
        }
    }

    internal readonly struct XmlModLoadingSnapshot
    {
        internal readonly string PackageId;
        internal readonly string ModName;
        internal readonly int Files;
        internal readonly long Bytes;
        internal readonly double DiscoveryMilliseconds;
        internal readonly double ParseMilliseconds;
        internal readonly double WaitMilliseconds;
        internal readonly bool Fallback;
        internal readonly bool Failed;

        internal XmlModLoadingSnapshot(
            string packageId,
            string modName,
            int files,
            long bytes,
            double discoveryMilliseconds,
            double parseMilliseconds,
            double waitMilliseconds,
            bool fallback,
            bool failed)
        {
            PackageId = packageId;
            ModName = modName;
            Files = files;
            Bytes = bytes;
            DiscoveryMilliseconds = discoveryMilliseconds;
            ParseMilliseconds = parseMilliseconds;
            WaitMilliseconds = waitMilliseconds;
            Fallback = fallback;
            Failed = failed;
        }
    }
}
