using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FixWorld.Loading;
using UnityEngine;
using Verse;

namespace FixWorld.Textures
{
    internal static partial class TextureDdsCache
    {
        private const string MaxCacheGiBEnvironmentVariable = "FIXWORLD_DDS_CACHE_MAX_GIB";
        private const string MinimumFreeGiBEnvironmentVariable = "FIXWORLD_DDS_CACHE_MIN_FREE_GIB";
        private const string WorkerCountEnvironmentVariable = "FIXWORLD_DDS_WORKERS";
        private const long DefaultMinimumFreeBytes = 10L * 1024L * 1024L * 1024L;

        private static readonly object Sync = new object();

        private static bool initialized;
        private static bool enabled;
        private static string cacheRoot;
        private static TextureDdsCacheBuilder builder;
        private static TextureCacheStore cacheStore;
        private static long maxCacheBytes;
        private static long minimumFreeBytes;
        private static long currentCacheBytes;
        private static long hitCount;
        private static long missCount;
        private static long createdCount;
        private static long invalidatedCount;
        private static long excludedCount;
        private static long unsupportedCount;
        private static long budgetSkippedCount;
        private static long failedCount;
        private static long buildMilliseconds;
        private static int workerCount;
        private static long workerPreparedMods;
        private static long workerAppliedMods;
        private static long workerFallbackMods;

        internal static void Initialize(string modRoot, FixWorldSettings settings)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            enabled = !string.Equals(
                Environment.GetEnvironmentVariable(
                    DdsCacheContract.EnabledEnvironmentVariable),
                "0",
                StringComparison.Ordinal);
            workerCount = ReadWorkerCount();
            if (!enabled)
            {
                Log.Message("[FixWorld] DDS cache disabled.");
                return;
            }

            LoadingOperation operation = LoadingEvents.Begin(
                Descriptor(
                    LoadingStage.Bootstrap,
                    LoadingStep.LoadTextureCache,
                    "Load texture cache index",
                    "Opening the DDS cache index",
                    affinity: LoadingThreadAffinity.WorkerSafe));
            try
            {
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
                long configuredMaximum = GiBToBytes(settings?.DdsCacheMaxGiB ?? 6.0f);
                maxCacheBytes = ReadGiBLimit(
                    MaxCacheGiBEnvironmentVariable,
                    configuredMaximum);
                minimumFreeBytes = ReadGiBLimit(
                    MinimumFreeGiBEnvironmentVariable,
                    DefaultMinimumFreeBytes);
                builder = new TextureDdsCacheBuilder(cacheRoot, modRoot);
                cacheStore = TextureCacheStore.Open(
                    cacheRoot,
                    DdsCacheContract.CacheIdentityVersion);
                currentCacheBytes = cacheStore.CurrentBytes;
                LogConfiguration();
            }
            catch (Exception exception)
            {
                operation.Fail();
                enabled = false;
                cacheStore?.Dispose();
                cacheStore = null;
                Log.Warning(
                    "[FixWorld] DDS cache disabled after initialization failure: " + exception);
            }
            finally
            {
                operation.Dispose();
            }
        }

        internal static void Apply(
            ModContentPack mod,
            string contentPath,
            List<string> foldersToLoadDebug,
            Dictionary<string, FileInfo> files)
        {
            if (!enabled ||
                !Prefs.TextureCompression ||
                foldersToLoadDebug != null ||
                files == null ||
                !string.Equals(
                    contentPath,
                    GenFilePaths.ContentPath<Texture2D>(),
                    StringComparison.Ordinal))
            {
                return;
            }

            lock (Sync)
            {
                bool prepared = HasPreparedPlan(mod);
                LoadingOperation operation = LoadingEvents.Begin(
                    Descriptor(
                        LoadingStage.Content,
                        prepared
                            ? LoadingStep.CommitTextureCache
                            : LoadingStep.ValidateTextureCache,
                        prepared
                            ? "Commit texture cache"
                            : "Validate texture cache",
                        prepared
                            ? "Applying prepared texture mapping for " + mod.Name
                            : "Checking cached textures for " + mod.Name,
                        LoadingModAttribution.Exact(mod),
                        mod.PackageId,
                        LoadingThreadAffinity.WorkerSafe));
                try
                {
                    if (TryApplyPrepared(mod, files))
                    {
                        return;
                    }

                    PrepareAndApplyFallback(mod, files);
                }
                catch (Exception exception)
                {
                    operation.Fail();
                    Log.Warning(
                        "[FixWorld] DDS cache skipped for " + mod.PackageId + ": " + exception);
                }
                finally
                {
                    operation.Dispose();
                }
            }
        }

        internal static void Complete()
        {
            lock (Sync)
            {
                if (!enabled || cacheStore == null)
                {
                    return;
                }

                try
                {
                    PrepareCacheMaintenance();
                    QueueDeferredBuild(TakeDeferredBuildEntries());
                }
                catch (Exception exception)
                {
                    Log.Warning("[FixWorld] DDS cache finalization failed: " + exception);
                }
                finally
                {
                    ClearPreparedPlans();
                    currentCacheBytes = cacheStore.CurrentBytes;
                }
            }
        }

        internal static TextureDdsCacheSnapshot GetSnapshot()
        {
            return new TextureDdsCacheSnapshot(
                enabled,
                Interlocked.Read(ref hitCount),
                Interlocked.Read(ref missCount),
                Interlocked.Read(ref createdCount),
                Interlocked.Read(ref invalidatedCount),
                Interlocked.Read(ref excludedCount),
                Interlocked.Read(ref unsupportedCount),
                Interlocked.Read(ref budgetSkippedCount),
                Interlocked.Read(ref failedCount),
                Interlocked.Read(ref buildMilliseconds),
                Interlocked.Read(ref currentCacheBytes),
                maxCacheBytes,
                workerCount,
                Interlocked.Read(ref workerPreparedMods),
                Interlocked.Read(ref workerAppliedMods),
                Interlocked.Read(ref workerFallbackMods));
        }


        private static void PrepareCacheMaintenance()
        {
            LoadingOperation pruneOperation = LoadingEvents.Begin(
                Descriptor(
                    LoadingStage.Finalize,
                    LoadingStep.PruneTextureCache,
                    "Prepare texture cache maintenance",
                    "Publishing active and in-budget DDS cache entries",
                    affinity: LoadingThreadAffinity.WorkerSafe));
            try
            {
                HashSet<string> activePackageIds = new HashSet<string>(
                    LoadedModManager.RunningModsListForReading
                        .Select(mod => Normalize(mod.PackageId)),
                    StringComparer.Ordinal);
                int removedEntries = cacheStore.RemoveInactivePackages(activePackageIds);
                int budgetRemovals = cacheStore.EnforceBudget(maxCacheBytes);
                Interlocked.Add(
                    ref invalidatedCount,
                    removedEntries + budgetRemovals);
            }
            catch
            {
                pruneOperation.Fail();
                throw;
            }
            finally
            {
                pruneOperation.Dispose();
            }

            currentCacheBytes = cacheStore.CurrentBytes;
        }


        private static void LogConfiguration()
        {
            string maxCacheDescription =
                ToGiB(maxCacheBytes).ToString("0.###", CultureInfo.InvariantCulture) + " GiB";
            if (builder.Available)
            {
                Log.Message(
                    "[FixWorld] DDS cache enabled at " + cacheRoot +
                    "; index=" + cacheStore.LoadStatus +
                    "; entries=" + cacheStore.EntryCount +
                    "; texconv=" + builder.TexconvPath +
                    "; ddsWorkers=" + workerCount +
                    "; maxCache=" + maxCacheDescription +
                    "; minFreeGiB=" +
                    ToGiB(minimumFreeBytes).ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }

            Log.Warning(
                "[FixWorld] DDS cache can read existing entries, but texconv was not found; " +
                "missing or changed entries will use their original textures. " +
                "Index=" + cacheStore.LoadStatus + ".");
        }

        private static LoadingStageEventDescriptor Descriptor(
            LoadingStage stage,
            LoadingStep step,
            string displayName,
            string activity,
            LoadingModAttribution? attribution = null,
            string subject = null,
            LoadingThreadAffinity affinity = LoadingThreadAffinity.MainThread)
        {
            return new LoadingStageEventDescriptor(
                stage,
                step,
                displayName,
                activity,
                subject,
                attribution ?? LoadingModAttribution.Global,
                affinity);
        }

        private static string GetRelativeSourcePath(FileInfo source, string modRoot)
        {
            return Normalize(source.FullName.Substring(modRoot.Length));
        }

        private static string GetContentCacheKey(
            string sourcePath,
            string sourceHash,
            string converterIdentity)
        {
            return GetTextHash(
                DdsCacheContract.CacheIdentityVersion + "\n" + sourcePath + "\n" +
                sourceHash + "\n" +
                (converterIdentity ?? "unavailable"));
        }

        private static string GetFileHash(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        private static string GetTextHash(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string ToHex(byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static long ReadGiBLimit(string environmentVariable, long defaultBytes)
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
                    out double gibibytes) || gibibytes <= 0.0)
            {
                throw new InvalidOperationException(
                    "Invalid positive GiB value in " + environmentVariable + ": " + value);
            }

            return GiBToBytes(gibibytes);
        }

        private static int ReadWorkerCount()
        {
            string value = Environment.GetEnvironmentVariable(WorkerCountEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return Math.Min(32, Math.Max(1, Environment.ProcessorCount / 2));
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                count < 0 ||
                count > 32)
            {
                throw new InvalidOperationException(
                    "Invalid worker count in " + WorkerCountEnvironmentVariable + ": " + value);
            }

            return count;
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

        private static double ToGiB(long bytes)
        {
            return bytes / (1024.0 * 1024.0 * 1024.0);
        }

        private static string SanitizePathSegment(string value)
        {
            HashSet<char> invalidCharacters = new HashSet<char>(
                Path.GetInvalidFileNameChars());
            return new string(Normalize(value)
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray());
        }

        private static string Normalize(string value)
        {
            return value.Replace('\\', '/').ToLowerInvariant();
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
    }
}
