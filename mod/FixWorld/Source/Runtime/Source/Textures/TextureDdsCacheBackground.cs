using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using FixWorld.Runtime;
using FixWorld.Scheduling;
using Verse;

namespace FixWorld.Textures
{
    internal sealed class TextureDdsCacheBackground
    {
        private const string BackgroundOutputEnvironmentVariable =
            "FIXWORLD_DDS_BACKGROUND_OUTPUT";

        private readonly object sync = new object();
        private readonly TextureCacheStore store;
        private readonly TextureDdsCacheBuilder builder;
        private readonly TextureDdsCacheMetrics metrics;
        private readonly int workerCount;
        private readonly JobScheduler scheduler;
        private readonly MainThreadQueue mainThread;
        private readonly List<JobHandle> jobs = new List<JobHandle>();

        private IReadOnlyList<TextureCacheEntry> pendingDeferredBuild =
            Array.Empty<TextureCacheEntry>();
        private bool started;
        private bool stopped;

        internal TextureDdsCacheBackground(
            TextureCacheStore store,
            TextureDdsCacheBuilder builder,
            TextureDdsCacheMetrics metrics,
            int workerCount,
            JobScheduler scheduler,
            MainThreadQueue mainThread)
        {
            this.store = store;
            this.builder = builder;
            this.metrics = metrics;
            this.workerCount = workerCount;
            this.scheduler = scheduler ??
                throw new ArgumentNullException(nameof(scheduler));
            this.mainThread = mainThread ??
                throw new ArgumentNullException(nameof(mainThread));
        }

        internal void Queue(IReadOnlyList<TextureCacheEntry> entries)
        {
            lock (sync)
            {
                if (stopped)
                {
                    return;
                }

                pendingDeferredBuild = entries ?? Array.Empty<TextureCacheEntry>();
            }
        }

        internal void Start()
        {
            IReadOnlyList<TextureCacheEntry> entries;
            lock (sync)
            {
                if (stopped || started)
                {
                    return;
                }

                started = true;
                entries = pendingDeferredBuild;
                pendingDeferredBuild = Array.Empty<TextureCacheEntry>();
            }

            long backgroundStartedAt = Stopwatch.GetTimestamp();
            TextureCacheEntry[][] batches = entries
                .GroupBy(entry => entry.PackageId, StringComparer.Ordinal)
                .Select(group => group.ToArray())
                .ToArray();
            string buildIdentity = TextureCacheIdentity.GetDeferredBuildIdentity(
                entries);
            int parallelism = Math.Max(
                1,
                Math.Min(workerCount, scheduler.WorkerCount));

            if (entries.Count == 0)
            {
                Track(scheduler.Schedule(
                    new Job<DeferredTextureCacheReport>(
                        "dds/maintenance/" + buildIdentity,
                        cancellationToken => RunDeferredMaintenance(
                            buildIdentity,
                            backgroundStartedAt,
                            cancellationToken),
                        name: "Clean deferred DDS cache",
                        lifetime: JobLifetime.Background,
                        priority: JobPriority.Low,
                        resourceClass: JobResourceClass.Io,
                        concurrencyKey: "dds-cache-writer",
                        maxConcurrency: 1)));
                return;
            }

            JobHandle<CacheBuildPreparation>[] preparations =
                new JobHandle<CacheBuildPreparation>[batches.Length];
            for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                TextureCacheEntry[] batch = batches[batchIndex];
                long estimatedBytes = batch.Sum(entry =>
                    Math.Max(0L, entry.EstimatedCacheBytes));
                preparations[batchIndex] = Track(scheduler.Schedule(
                    new Job<CacheBuildPreparation>(
                        "dds/prepare/" + buildIdentity + "/" + batchIndex,
                        cancellationToken =>
                            builder.Prepare(batch, cancellationToken),
                        name: "Build DDS for " + batch[0].PackageId,
                        lifetime: JobLifetime.Background,
                        priority: JobPriority.Low,
                        resourceClass: JobResourceClass.Mixed,
                        estimatedBytes: estimatedBytes,
                        concurrencyKey: "dds-build",
                        maxConcurrency: parallelism)));
            }

            Track(scheduler.Schedule(
                new Job<DeferredTextureCacheReport>(
                    "dds/publish/" + buildIdentity,
                    cancellationToken => RunDeferredBuild(
                        entries,
                        batches,
                        preparations,
                        buildIdentity,
                        backgroundStartedAt,
                        cancellationToken),
                    name: "Publish deferred DDS cache",
                    lifetime: JobLifetime.Background,
                    priority: JobPriority.Low,
                    resourceClass: JobResourceClass.Io,
                    dependencies: preparations.Cast<JobHandle>().ToArray(),
                    concurrencyKey: "dds-cache-writer",
                    maxConcurrency: 1)));
        }

        internal bool Shutdown()
        {
            JobHandle[] scheduled;
            lock (sync)
            {
                stopped = true;
                pendingDeferredBuild = Array.Empty<TextureCacheEntry>();
                scheduled = jobs.ToArray();
            }

            foreach (JobHandle handle in scheduled)
            {
                scheduler.Cancel(handle);
            }

            using (CancellationTokenSource timeout =
                   new CancellationTokenSource(2000))
            {
                try
                {
                    foreach (JobHandle handle in scheduled)
                    {
                        if (!handle.IsTerminal)
                        {
                            handle.Wait(timeout.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            return scheduled.All(handle => handle.IsTerminal);
        }

        private THandle Track<THandle>(THandle handle)
            where THandle : JobHandle
        {
            lock (sync)
            {
                if (stopped)
                {
                    scheduler.Cancel(handle);
                }
                else
                {
                    jobs.Add(handle);
                }
            }

            return handle;
        }

        private DeferredTextureCacheReport RunDeferredMaintenance(
            string buildIdentity,
            long backgroundStartedAt,
            CancellationToken cancellationToken)
        {
            int removedOrphans = 0;
            Exception terminalError = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                removedOrphans = store.SweepOrphans();
                store.Save();
                metrics.AddInvalidated(removedOrphans);
                metrics.SetCacheBytes(store.CurrentBytes);
            }
            catch (Exception exception)
            {
                terminalError = exception;
            }
            finally
            {
                DeferredTextureCacheReport report = CreateDeferredReport(
                    0,
                    0,
                    terminalError == null ? 0 : 1,
                    removedOrphans,
                    backgroundStartedAt);
                string reportError = WriteDeferredReport(report);
                QueueDeferredCompletionLog(
                    buildIdentity,
                    report,
                    Array.Empty<string>(),
                    terminalError,
                    reportError);
            }

            if (terminalError != null)
            {
                throw terminalError;
            }

            return CreateDeferredReport(
                0,
                0,
                0,
                removedOrphans,
                backgroundStartedAt);
        }

        private DeferredTextureCacheReport RunDeferredBuild(
            IReadOnlyList<TextureCacheEntry> entries,
            IReadOnlyList<TextureCacheEntry[]> batches,
            IReadOnlyList<JobHandle<CacheBuildPreparation>> preparations,
            string buildIdentity,
            long backgroundStartedAt,
            CancellationToken cancellationToken)
        {
            int created = 0;
            int failed = 0;
            int removedOrphans = 0;
            Exception terminalError = null;
            List<string> warnings = new List<string>();
            try
            {
                for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TextureCacheEntry[] batch = batches[batchIndex];
                    CacheBuildResult result = builder.Publish(
                        preparations[batchIndex].Result);
                    created += result.Created;
                    failed += result.Failed;
                    if (result.Error != null)
                    {
                        warnings.Add(batch[0].PackageId + ": " + result.Error);
                    }

                    foreach (TextureCacheEntry entry in batch)
                    {
                        if (!File.Exists(entry.FinalPath))
                        {
                            continue;
                        }

                        store.RegisterExisting(
                            entry.PackageId,
                            entry.SourcePath,
                            entry.Source,
                            entry.SourceHash,
                            entry.ConverterIdentity,
                            entry.FinalPath,
                            createdAfterOpen: true);
                    }

                    if ((batchIndex + 1) % 8 == 0)
                    {
                        store.Save();
                    }
                }

                removedOrphans = store.SweepOrphans();
                store.Save();
                metrics.AddInvalidated(removedOrphans);
                metrics.SetCacheBytes(store.CurrentBytes);
            }
            catch (Exception exception)
            {
                failed = Math.Max(failed, entries.Count - created);
                terminalError = exception;
            }
            finally
            {
                double backgroundMilliseconds = GetElapsedMilliseconds(
                    backgroundStartedAt);
                metrics.CompleteBuild(created, failed, backgroundMilliseconds);
                DeferredTextureCacheReport report =
                    new DeferredTextureCacheReport(
                        entries.Count,
                        created,
                        failed,
                        backgroundMilliseconds,
                        Math.Max(1, Math.Min(
                            workerCount,
                            scheduler.WorkerCount)),
                        removedOrphans);
                string reportError = WriteDeferredReport(report);
                QueueDeferredCompletionLog(
                    buildIdentity,
                    report,
                    warnings,
                    terminalError,
                    reportError);
            }

            if (terminalError != null)
            {
                throw terminalError;
            }

            return CreateDeferredReport(
                entries.Count,
                created,
                failed,
                removedOrphans,
                backgroundStartedAt);
        }

        private DeferredTextureCacheReport CreateDeferredReport(
            int entries,
            int created,
            int failed,
            int removedOrphans,
            long backgroundStartedAt)
        {
            return new DeferredTextureCacheReport(
                entries,
                created,
                failed,
                GetElapsedMilliseconds(backgroundStartedAt),
                Math.Max(1, Math.Min(
                    workerCount,
                    scheduler.WorkerCount)),
                removedOrphans);
        }

        private static double GetElapsedMilliseconds(long startedAt)
        {
            return (Stopwatch.GetTimestamp() - startedAt) *
                   1000.0 / Stopwatch.Frequency;
        }

        private static string WriteDeferredReport(DeferredTextureCacheReport report)
        {
            string path = Environment.GetEnvironmentVariable(
                BackgroundOutputEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(DeferredTextureCacheReport));
                AtomicFile.Write(
                    path,
                    stream => serializer.WriteObject(stream, report));

                return null;
            }
            catch (Exception exception)
            {
                return "[FixWorld] Could not write deferred DDS report: " + exception;
            }
        }

        private void QueueDeferredCompletionLog(
            string buildIdentity,
            DeferredTextureCacheReport report,
            IReadOnlyList<string> warnings,
            Exception terminalError,
            string reportError)
        {
            try
            {
                mainThread.Post(
                    "Report deferred DDS cache",
                    () =>
                    {
                        foreach (string warning in warnings.Take(3))
                        {
                            Log.Warning("[FixWorld] Deferred DDS build: " + warning);
                        }

                        if (terminalError != null)
                        {
                            Log.Warning(
                                "[FixWorld] Deferred DDS cache build failed: " +
                                terminalError);
                        }

                        if (reportError != null)
                        {
                            Log.Warning(reportError);
                        }

                        Log.Message(
                            "[FixWorld] Deferred DDS cache build complete: " +
                            report.Created.ToString(CultureInfo.InvariantCulture) +
                            " created, " +
                            report.Failed.ToString(CultureInfo.InvariantCulture) +
                            " failed in " +
                            (report.Milliseconds / 1000.0).ToString(
                                "F1",
                                CultureInfo.InvariantCulture) +
                            " s, " +
                            report.RemovedOrphans.ToString(
                                CultureInfo.InvariantCulture) +
                            " orphan files removed.");
                    });
            }
            catch (ObjectDisposedException)
            {
                // RimWorld is already shutting down.
            }
        }

    }

    [DataContract]
    internal sealed class DeferredTextureCacheReport
    {
        [DataMember(Name = "entries", Order = 1)]
        public int Entries { get; private set; }

        [DataMember(Name = "created", Order = 2)]
        public int Created { get; private set; }

        [DataMember(Name = "failed", Order = 3)]
        public int Failed { get; private set; }

        [DataMember(Name = "milliseconds", Order = 4)]
        public double Milliseconds { get; private set; }

        [DataMember(Name = "workers", Order = 5)]
        public int Workers { get; private set; }

        [DataMember(Name = "removedOrphans", Order = 6)]
        public int RemovedOrphans { get; private set; }

        internal DeferredTextureCacheReport(
            int entries,
            int created,
            int failed,
            double milliseconds,
            int workers,
            int removedOrphans)
        {
            Entries = entries;
            Created = created;
            Failed = failed;
            Milliseconds = milliseconds;
            Workers = workers;
            RemovedOrphans = removedOrphans;
        }
    }
}
