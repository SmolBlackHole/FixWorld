using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FixWorld.Scheduling;
using Verse;

namespace FixWorld.Caching
{
    internal static partial class TextureDdsCache
    {
        private const string BackgroundOutputEnvironmentVariable =
            "FIXWORLD_DDS_BACKGROUND_OUTPUT";

        private static readonly object BackgroundSync = new object();

        private static IReadOnlyList<TextureCacheEntry> pendingDeferredBuild =
            Array.Empty<TextureCacheEntry>();
        private static bool deferredBuildStarted;

        private static void QueueDeferredBuild(IReadOnlyList<TextureCacheEntry> entries)
        {
            lock (BackgroundSync)
            {
                pendingDeferredBuild = entries ?? Array.Empty<TextureCacheEntry>();
            }
        }

        internal static void StartDeferredBuild()
        {
            IReadOnlyList<TextureCacheEntry> entries;
            lock (BackgroundSync)
            {
                if (deferredBuildStarted)
                {
                    return;
                }

                deferredBuildStarted = true;
                entries = pendingDeferredBuild;
                pendingDeferredBuild = Array.Empty<TextureCacheEntry>();
            }

            if (entries.Count == 0)
            {
                string reportError = WriteDeferredReport(
                    new DeferredTextureCacheReport(
                        0,
                        0,
                        0,
                        0.0,
                        FixWorldScheduler.WorkerCount));
                if (reportError != null)
                {
                    Log.Warning(reportError);
                }

                return;
            }

            TextureCacheEntry[][] batches = entries
                .GroupBy(entry => entry.PackageId, StringComparer.Ordinal)
                .Select(group => group.ToArray())
                .ToArray();
            long backgroundStartedAt = Stopwatch.GetTimestamp();
            string buildIdentity = GetDeferredBuildIdentity(entries);
            int parallelism = Math.Max(
                1,
                Math.Min(workerCount, FixWorldScheduler.WorkerCount));
            ScheduledJobHandle<CacheBuildPreparation>[] preparations =
                new ScheduledJobHandle<CacheBuildPreparation>[batches.Length];
            for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                TextureCacheEntry[] batch = batches[batchIndex];
                long estimatedBytes = batch.Sum(entry =>
                    Math.Max(0L, entry.EstimatedCacheBytes));
                preparations[batchIndex] = FixWorldScheduler.Schedule(
                    new SchedulerJob<CacheBuildPreparation>(
                        "dds/prepare/" + buildIdentity + "/" + batchIndex,
                        "Build DDS for " + batch[0].PackageId,
                        SchedulerJobLifetime.Background,
                        SchedulerJobPriority.Low,
                        SchedulerResourceClass.Mixed,
                        cancellationToken =>
                            builder.Prepare(batch, cancellationToken),
                        estimatedBytes: estimatedBytes,
                        concurrencyKey: "dds-build",
                        maxConcurrency: parallelism));
            }

            FixWorldScheduler.Schedule(
                new SchedulerJob<DeferredTextureCacheReport>(
                    "dds/publish/" + buildIdentity,
                    "Publish deferred DDS cache",
                    SchedulerJobLifetime.Background,
                    SchedulerJobPriority.Low,
                    SchedulerResourceClass.Io,
                    cancellationToken => RunDeferredBuild(
                        entries,
                        batches,
                        preparations,
                        buildIdentity,
                        backgroundStartedAt,
                        cancellationToken),
                    dependencies: preparations,
                    concurrencyKey: "dds-index-writer",
                    maxConcurrency: 1));
        }

        private static DeferredTextureCacheReport RunDeferredBuild(
            IReadOnlyList<TextureCacheEntry> entries,
            IReadOnlyList<TextureCacheEntry[]> batches,
            IReadOnlyList<ScheduledJobHandle<CacheBuildPreparation>> preparations,
            string buildIdentity,
            long backgroundStartedAt,
            CancellationToken cancellationToken)
        {
            int created = 0;
            int failed = 0;
            Exception terminalError = null;
            List<string> warnings = new List<string>();
            try
            {
                using (TextureCacheIndex backgroundIndex =
                       TextureCacheIndex.Open(cacheRoot, CacheIdentityVersion))
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
                            warnings.Add(
                                batch[0].PackageId + ": " + result.Error);
                        }

                        foreach (TextureCacheEntry entry in batch)
                        {
                            if (!File.Exists(entry.FinalPath))
                            {
                                continue;
                            }

                            backgroundIndex.RegisterExisting(
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
                            backgroundIndex.Save();
                        }
                    }

                    backgroundIndex.Save();
                    Interlocked.Exchange(ref currentCacheBytes, backgroundIndex.CurrentBytes);
                }
            }
            catch (Exception exception)
            {
                failed = Math.Max(failed, entries.Count - created);
                terminalError = exception;
            }
            finally
            {
                double backgroundMilliseconds =
                    (Stopwatch.GetTimestamp() - backgroundStartedAt) *
                    1000.0 / Stopwatch.Frequency;
                Interlocked.Add(ref createdCount, created);
                Interlocked.Add(ref failedCount, failed);
                Interlocked.Add(
                    ref buildMilliseconds,
                    (long)Math.Round(backgroundMilliseconds));
                DeferredTextureCacheReport report =
                    new DeferredTextureCacheReport(
                        entries.Count,
                        created,
                        failed,
                        backgroundMilliseconds,
                        Math.Max(
                            1,
                            Math.Min(workerCount, FixWorldScheduler.WorkerCount)));
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

            return new DeferredTextureCacheReport(
                entries.Count,
                created,
                failed,
                (Stopwatch.GetTimestamp() - backgroundStartedAt) *
                1000.0 / Stopwatch.Frequency,
                Math.Max(1, Math.Min(workerCount, FixWorldScheduler.WorkerCount)));
        }

        private static string WriteDeferredReport(DeferredTextureCacheReport report)
        {
            string path = Environment.GetEnvironmentVariable(
                BackgroundOutputEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string resolvedPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = resolvedPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(DeferredTextureCacheReport));
                using (FileStream stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    serializer.WriteObject(stream, report);
                    stream.Flush(true);
                }

                if (File.Exists(resolvedPath))
                {
                    File.Replace(temporaryPath, resolvedPath, null);
                }
                else
                {
                    File.Move(temporaryPath, resolvedPath);
                }

                return null;
            }
            catch (Exception exception)
            {
                return "[FixWorld] Could not write deferred DDS report: " + exception;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void QueueDeferredCompletionLog(
            string buildIdentity,
            DeferredTextureCacheReport report,
            IReadOnlyList<string> warnings,
            Exception terminalError,
            string reportError)
        {
            try
            {
                FixWorldScheduler.Dispatch(
                    "dds/log/" + buildIdentity,
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
                            " s.");
                    });
            }
            catch (ObjectDisposedException)
            {
                // RimWorld is already shutting down.
            }
        }

        private static string GetDeferredBuildIdentity(
            IReadOnlyList<TextureCacheEntry> entries)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                StringBuilder input = new StringBuilder(entries.Count * 96);
                foreach (TextureCacheEntry entry in entries)
                {
                    input.Append(entry.Key)
                        .Append('|')
                        .Append(entry.SourceHash)
                        .Append('\n');
                }

                byte[] hash = sha256.ComputeHash(
                    Encoding.UTF8.GetBytes(input.ToString()));
                return BitConverter.ToString(hash)
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
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

        internal DeferredTextureCacheReport(
            int entries,
            int created,
            int failed,
            double milliseconds,
            int workers)
        {
            Entries = entries;
            Created = created;
            Failed = failed;
            Milliseconds = milliseconds;
            Workers = workers;
        }
    }
}
