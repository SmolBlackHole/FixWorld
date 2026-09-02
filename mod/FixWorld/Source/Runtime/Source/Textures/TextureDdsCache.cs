using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using FixWorld.Scheduling;
using UnityEngine;
using Verse;

namespace FixWorld.Textures
{
    internal sealed class TextureDdsCache
    {
        private readonly object sync = new object();
        private readonly JobScheduler scheduler;
        private readonly MainThreadQueue mainThread;

        private bool attached;
        private TextureDdsCacheRuntime runtime;
        private TextureDdsCacheSnapshot lastSnapshot;

        internal TextureDdsCache(
            JobScheduler scheduler,
            MainThreadQueue mainThread)
        {
            this.scheduler = scheduler ??
                throw new ArgumentNullException(nameof(scheduler));
            this.mainThread = mainThread ??
                throw new ArgumentNullException(nameof(mainThread));
        }

        internal void Attach(
            string modRoot,
            float ddsCacheMaxGiB)
        {
            lock (sync)
            {
                if (attached)
                {
                    return;
                }

                attached = true;
                int workerCount = TextureDdsCacheConfiguration.ReadWorkerCount();
                lastSnapshot = TextureDdsCacheSnapshot.Disabled(workerCount);
                if (!TextureDdsCacheConfiguration.IsEnabled())
                {
                    Log.Message("[FixWorld] DDS cache disabled.");
                    return;
                }

                try
                {
                    runtime = TextureDdsCacheRuntime.Open(
                        modRoot,
                        ddsCacheMaxGiB,
                        workerCount,
                        scheduler,
                        mainThread);
                    lastSnapshot = runtime.GetSnapshot();
                }
                catch (Exception exception)
                {
                    runtime?.Shutdown();
                    runtime = null;
                    Log.Warning(
                        "[FixWorld] DDS cache disabled after initialization failure: " +
                        exception);
                }
            }
        }

        internal void Apply(
            ModContentPack mod,
            string contentPath,
            List<string> foldersToLoadDebug,
            Dictionary<string, FileInfo> files)
        {
            Volatile.Read(ref runtime)?.Apply(
                mod,
                contentPath,
                foldersToLoadDebug,
                files);
        }

        internal void Complete()
        {
            Volatile.Read(ref runtime)?.Complete();
        }

        internal void StartDeferredBuild()
        {
            Volatile.Read(ref runtime)?.StartDeferredBuild();
        }

        internal void Shutdown()
        {
            TextureDdsCacheRuntime current;
            lock (sync)
            {
                current = runtime;
                runtime = null;
                if (current != null)
                {
                    lastSnapshot = current.GetSnapshot(enabled: false);
                }
            }

            current?.Shutdown();
        }

        internal TextureDdsCacheSnapshot GetSnapshot()
        {
            TextureDdsCacheRuntime current = Volatile.Read(ref runtime);
            return current?.GetSnapshot() ?? lastSnapshot;
        }
    }

    internal sealed class TextureDdsCacheRuntime
    {
        private readonly object sync = new object();
        private readonly string cacheRoot;
        private readonly TextureDdsCacheBuilder builder;
        private readonly TextureCacheStore store;
        private readonly TextureDdsCacheMetrics metrics;
        private readonly TextureDdsCachePlanner planner;
        private readonly TextureDdsCacheBackground background;
        private readonly long maxCacheBytes;
        private readonly long minimumFreeBytes;
        private readonly int workerCount;

        private bool stopped;

        private TextureDdsCacheRuntime(
            string cacheRoot,
            TextureDdsCacheBuilder builder,
            TextureCacheStore store,
            long maxCacheBytes,
            long minimumFreeBytes,
            int workerCount,
            JobScheduler scheduler,
            MainThreadQueue mainThread)
        {
            this.cacheRoot = cacheRoot;
            this.builder = builder;
            this.store = store;
            this.maxCacheBytes = maxCacheBytes;
            this.minimumFreeBytes = minimumFreeBytes;
            this.workerCount = workerCount;
            metrics = new TextureDdsCacheMetrics();
            metrics.SetCacheBytes(store.CurrentBytes);
            planner = new TextureDdsCachePlanner(
                cacheRoot,
                store,
                builder,
                metrics,
                maxCacheBytes,
                minimumFreeBytes);
            background = new TextureDdsCacheBackground(
                store,
                builder,
                metrics,
                workerCount,
                scheduler,
                mainThread);
        }

        internal static TextureDdsCacheRuntime Open(
            string modRoot,
            float ddsCacheMaxGiB,
            int workerCount,
            JobScheduler scheduler,
            MainThreadQueue mainThread)
        {
            string cacheRoot = TextureDdsCacheConfiguration.ResolveCacheRoot();
            long maxCacheBytes = TextureDdsCacheConfiguration.ReadMaximumCacheBytes(
                ddsCacheMaxGiB);
            long minimumFreeBytes =
                TextureDdsCacheConfiguration.ReadMinimumFreeBytes();
            TextureDdsCacheBuilder builder = new TextureDdsCacheBuilder(
                cacheRoot,
                modRoot);
            TextureCacheStore store = null;
            try
            {
                store = TextureCacheStore.Open(
                    cacheRoot,
                    DdsCacheContract.CacheIdentityVersion);
                TextureDdsCacheRuntime result = new TextureDdsCacheRuntime(
                    cacheRoot,
                    builder,
                    store,
                    maxCacheBytes,
                    minimumFreeBytes,
                    workerCount,
                    scheduler,
                    mainThread);
                result.LogConfiguration();
                return result;
            }
            catch
            {
                store?.Dispose();
                throw;
            }
        }

        internal void Apply(
            ModContentPack mod,
            string contentPath,
            List<string> foldersToLoadDebug,
            Dictionary<string, FileInfo> files)
        {
            lock (sync)
            {
                if (stopped)
                {
                    return;
                }
            }

            if (!Prefs.TextureCompression ||
                foldersToLoadDebug != null ||
                files == null ||
                !string.Equals(
                    contentPath,
                    GenFilePaths.ContentPath<Texture2D>(),
                    StringComparison.Ordinal))
            {
                return;
            }

            planner.Apply(mod, files);
        }

        internal void Complete()
        {
            lock (sync)
            {
                if (stopped)
                {
                    return;
                }
            }

            try
            {
                background.Queue(planner.Complete());
            }
            catch (Exception exception)
            {
                Log.Warning("[FixWorld] DDS cache finalization failed: " + exception);
            }
        }

        internal void StartDeferredBuild()
        {
            lock (sync)
            {
                if (stopped)
                {
                    return;
                }
            }

            background.Start();
        }

        internal TextureDdsCacheSnapshot GetSnapshot(bool enabled = true)
        {
            return metrics.GetSnapshot(
                enabled,
                maxCacheBytes,
                workerCount);
        }

        internal void Shutdown()
        {
            lock (sync)
            {
                if (stopped)
                {
                    return;
                }

                stopped = true;
            }

            if (!background.Shutdown())
            {
                Log.Warning(
                    "[FixWorld] DDS jobs did not stop within two seconds; " +
                    "cache resources remain open until process exit.");
                return;
            }

            planner.Shutdown();
            try
            {
                store.Save();
            }
            finally
            {
                store.Dispose();
            }
        }

        private void LogConfiguration()
        {
            string maxCacheDescription =
                TextureDdsCacheConfiguration.ToGiB(maxCacheBytes)
                    .ToString("0.###", CultureInfo.InvariantCulture) + " GiB";
            if (builder.Available)
            {
                Log.Message(
                    "[FixWorld] DDS cache enabled at " + cacheRoot +
                    "; index=" + store.LoadStatus +
                    "; entries=" + store.EntryCount +
                    "; texconv=" + builder.TexconvPath +
                    "; ddsWorkers=" + workerCount +
                    "; maxCache=" + maxCacheDescription +
                    "; minFreeGiB=" +
                    TextureDdsCacheConfiguration.ToGiB(minimumFreeBytes)
                        .ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }

            Log.Warning(
                "[FixWorld] DDS cache can read existing entries, but texconv was not found; " +
                "missing or changed entries will use their original textures. " +
                "Index=" + store.LoadStatus + ".");
        }
    }

    internal sealed class TextureDdsCacheMetrics
    {
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

        internal void Hit() => Interlocked.Increment(ref hits);
        internal void Miss() => Interlocked.Increment(ref misses);
        internal void Exclude() => Interlocked.Increment(ref excluded);
        internal void Unsupported() => Interlocked.Increment(ref unsupported);
        internal void PreparedMod() => Interlocked.Increment(ref workerPreparedMods);
        internal void AppliedMod() => Interlocked.Increment(ref workerAppliedMods);
        internal void FallbackMod() => Interlocked.Increment(ref workerFallbackMods);

        internal void AddInvalidated(long count) =>
            Interlocked.Add(ref invalidated, count);

        internal void AddBudgetSkipped(long count) =>
            Interlocked.Add(ref budgetSkipped, count);

        internal void CompleteBuild(
            int createdCount,
            int failedCount,
            double milliseconds)
        {
            Interlocked.Add(ref created, createdCount);
            Interlocked.Add(ref failed, failedCount);
            Interlocked.Add(
                ref buildMilliseconds,
                (long)Math.Round(milliseconds));
        }

        internal void SetCacheBytes(long bytes) =>
            Interlocked.Exchange(ref cacheBytes, bytes);

        internal TextureDdsCacheSnapshot GetSnapshot(
            bool enabled,
            long maxCacheBytes,
            int workerCount)
        {
            return new TextureDdsCacheSnapshot(
                enabled,
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
                maxCacheBytes,
                workerCount,
                Interlocked.Read(ref workerPreparedMods),
                Interlocked.Read(ref workerAppliedMods),
                Interlocked.Read(ref workerFallbackMods));
        }
    }

    internal static class TextureDdsCacheConfiguration
    {
        private const string MaxCacheGiBEnvironmentVariable =
            "FIXWORLD_DDS_CACHE_MAX_GIB";
        private const string MinimumFreeGiBEnvironmentVariable =
            "FIXWORLD_DDS_CACHE_MIN_FREE_GIB";
        private const string WorkerCountEnvironmentVariable = "FIXWORLD_DDS_WORKERS";
        private const long DefaultMinimumFreeBytes =
            10L * 1024L * 1024L * 1024L;

        internal static bool IsEnabled()
        {
            return !string.Equals(
                Environment.GetEnvironmentVariable(
                    DdsCacheContract.EnabledEnvironmentVariable),
                "0",
                StringComparison.Ordinal);
        }

        internal static string ResolveCacheRoot()
        {
            string root = Environment.GetEnvironmentVariable(
                DdsCacheContract.CacheRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(
                    GenFilePaths.SaveDataFolderPath,
                    "FixWorld",
                    "TextureCache",
                    DdsCacheContract.CacheDirectoryName);
            }

            root = Path.GetFullPath(root);
            Directory.CreateDirectory(root);
            return root;
        }

        internal static long ReadMaximumCacheBytes(float ddsCacheMaxGiB)
        {
            long configuredMaximum = GiBToBytes(ddsCacheMaxGiB);
            return ReadGiBLimit(
                MaxCacheGiBEnvironmentVariable,
                configuredMaximum);
        }

        internal static long ReadMinimumFreeBytes()
        {
            return ReadGiBLimit(
                MinimumFreeGiBEnvironmentVariable,
                DefaultMinimumFreeBytes);
        }

        internal static int ReadWorkerCount()
        {
            string value = Environment.GetEnvironmentVariable(
                WorkerCountEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return Math.Min(32, Math.Max(1, Environment.ProcessorCount / 2));
            }

            if (!int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int count) ||
                count < 0 ||
                count > 32)
            {
                throw new InvalidOperationException(
                    "Invalid worker count in " + WorkerCountEnvironmentVariable +
                    ": " + value);
            }

            return count;
        }

        internal static double ToGiB(long bytes)
        {
            return bytes / (1024.0 * 1024.0 * 1024.0);
        }

        private static long ReadGiBLimit(
            string environmentVariable,
            long defaultBytes)
        {
            string value = Environment.GetEnvironmentVariable(environmentVariable);
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
                    "Invalid positive GiB value in " + environmentVariable +
                    ": " + value);
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

    internal readonly struct TextureDdsCacheSnapshot
    {
        internal readonly bool Enabled;
        internal readonly long Hits;
        internal readonly long Misses;
        internal readonly long Created;
        internal readonly long Invalidated;
        internal readonly long Excluded;
        internal readonly long Unsupported;
        internal readonly long BudgetSkipped;
        internal readonly long Failed;
        internal readonly long BuildMilliseconds;
        internal readonly long CacheBytes;
        internal readonly long MaxCacheBytes;
        internal readonly int WorkerCount;
        internal readonly long WorkerPreparedMods;
        internal readonly long WorkerAppliedMods;
        internal readonly long WorkerFallbackMods;

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
