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

namespace FixWorld.Caching
{
    internal static class TextureDdsCache
    {
        private const string CacheIdentityVersion = "bc3-unorm-mips-v3-content-index";
        private const string LegacyCacheIdentityVersion = "bc3-unorm-mips-v2-vflip";
        private const string EnabledEnvironmentVariable = "FIXWORLD_DDS_CACHE";
        private const string CacheRootEnvironmentVariable = "FIXWORLD_DDS_CACHE_ROOT";
        private const string MaxCacheGiBEnvironmentVariable = "FIXWORLD_DDS_CACHE_MAX_GIB";
        private const string MinimumFreeGiBEnvironmentVariable = "FIXWORLD_DDS_CACHE_MIN_FREE_GIB";
        private const long DefaultMinimumFreeBytes = 10L * 1024L * 1024L * 1024L;

        private static readonly object Sync = new object();

        private static bool initialized;
        private static bool enabled;
        private static string cacheRoot;
        private static TextureDdsCacheBuilder builder;
        private static TextureCacheIndex index;
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

        internal static void Initialize(string modRoot, FixWorldSettings settings)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            enabled = !string.Equals(
                Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
                "0",
                StringComparison.Ordinal);
            if (!enabled)
            {
                Log.Message("[FixWorld] DDS cache disabled.");
                return;
            }

            LoadingStageOperation operation = LoadingStageMailbox.Begin(
                Descriptor(
                    LoadingStage.Bootstrap,
                    LoadingStep.LoadTextureCacheIndex,
                    "Load texture cache index",
                    "Opening and validating the DDS cache index",
                    affinity: LoadingThreadAffinity.WorkerSafe));
            try
            {
                cacheRoot = Environment.GetEnvironmentVariable(CacheRootEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(cacheRoot))
                {
                    cacheRoot = Path.Combine(
                        GenFilePaths.SaveDataFolderPath,
                        "FixWorld",
                        "TextureCache",
                        "dds-v1");
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
                index = TextureCacheIndex.Open(cacheRoot, CacheIdentityVersion);
                currentCacheBytes = index.CurrentBytes;
                LogConfiguration();
            }
            catch (Exception exception)
            {
                operation.Fail();
                enabled = false;
                index?.Dispose();
                index = null;
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
                LoadingStageOperation operation = LoadingStageMailbox.Begin(
                    Descriptor(
                        LoadingStage.Content,
                        LoadingStep.ValidateTextureCache,
                        "Validate texture cache",
                        "Checking cached textures for " + mod.Name,
                        LoadingModAttribution.Exact(mod),
                        mod.PackageId,
                        LoadingThreadAffinity.WorkerSafe));
                try
                {
                    ApplyCore(mod, files, operation);
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
                if (!enabled || index == null)
                {
                    return;
                }

                try
                {
                    PruneAndSave();
                }
                catch (Exception exception)
                {
                    Log.Warning("[FixWorld] DDS cache finalization failed: " + exception);
                }
                finally
                {
                    currentCacheBytes = index.CurrentBytes;
                    index.Dispose();
                    index = null;
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
                maxCacheBytes);
        }

        private static void ApplyCore(
            ModContentPack mod,
            Dictionary<string, FileInfo> files,
            LoadingStageOperation operation)
        {
            string packageId = Normalize(mod.PackageId);
            string packageCacheRoot = Path.Combine(cacheRoot, SanitizePathSegment(packageId));
            string modRoot = Path.GetFullPath(mod.RootDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
            HashSet<string> shippedDdsPaths = new HashSet<string>(
                files.Keys
                    .Select(Normalize)
                    .Where(path => path.EndsWith(".dds", StringComparison.Ordinal)),
                StringComparer.Ordinal);
            HashSet<string> retainedSourcePaths = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> desiredDirectories = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            List<TextureCacheEntry> missingEntries = new List<TextureCacheEntry>();
            List<KeyValuePair<string, FileInfo>> sourceFiles = files.ToList();

            for (int sourceIndex = 0; sourceIndex < sourceFiles.Count; sourceIndex++)
            {
                KeyValuePair<string, FileInfo> item = sourceFiles[sourceIndex];
                operation.ReportProgress(
                    sourceIndex + 1,
                    sourceFiles.Count,
                    "Checking cached textures for " + mod.Name);
                FileInfo source = item.Value;
                string sourceKey = Normalize(item.Key);
                string extension = source.Extension.ToLowerInvariant();
                if (extension == ".dds" ||
                    shippedDdsPaths.Contains(Path.ChangeExtension(sourceKey, ".dds")) ||
                    !source.FullName.StartsWith(modRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                {
                    continue;
                }

                string sourcePath = GetRelativeSourcePath(source, modRoot);
                if (index.TryGetFresh(
                        packageId,
                        sourcePath,
                        source,
                        builder.Identity,
                        out string freshPath))
                {
                    retainedSourcePaths.Add(sourcePath);
                    desiredDirectories.Add(Path.GetDirectoryName(freshPath));
                    files[item.Key] = new FileInfo(freshPath);
                    Interlocked.Increment(ref hitCount);
                    continue;
                }

                if (!TextureDimensions.TryRead(source, out TextureDimensions dimensions))
                {
                    Interlocked.Increment(ref unsupportedCount);
                    index.RemoveSource(packageId, sourcePath);
                    continue;
                }

                int mipCount = dimensions.GetBc3MipCount();
                if (mipCount == 0)
                {
                    Interlocked.Increment(ref excludedCount);
                    index.RemoveSource(packageId, sourcePath);
                    continue;
                }

                retainedSourcePaths.Add(sourcePath);
                if (TryRecoverLegacyEntry(
                        packageId,
                        sourcePath,
                        source,
                        modRoot,
                        packageCacheRoot,
                        out string legacyPath))
                {
                    desiredDirectories.Add(Path.GetDirectoryName(legacyPath));
                    files[item.Key] = new FileInfo(legacyPath);
                    Interlocked.Increment(ref hitCount);
                    continue;
                }

                string sourceHash = GetFileHash(source.FullName);
                if (index.TryReuseContent(
                        packageId,
                        sourcePath,
                        source,
                        sourceHash,
                        builder.Identity,
                        out string reusablePath))
                {
                    desiredDirectories.Add(Path.GetDirectoryName(reusablePath));
                    files[item.Key] = new FileInfo(reusablePath);
                    Interlocked.Increment(ref hitCount);
                    continue;
                }

                index.RemoveSource(packageId, sourcePath);
                string cacheKey = GetContentCacheKey(
                    sourcePath,
                    sourceHash,
                    builder.Identity);
                string finalDirectory = Path.Combine(packageCacheRoot, cacheKey);
                string finalPath = Path.Combine(
                    finalDirectory,
                    Path.GetFileNameWithoutExtension(source.Name) + ".dds");
                desiredDirectories.Add(finalDirectory);
                if (File.Exists(finalPath))
                {
                    index.RegisterExisting(
                        packageId,
                        sourcePath,
                        source,
                        sourceHash,
                        builder.Identity,
                        finalPath,
                        createdAfterOpen: false);
                    files[item.Key] = new FileInfo(finalPath);
                    Interlocked.Increment(ref hitCount);
                    continue;
                }

                missingEntries.Add(new TextureCacheEntry(
                    item.Key,
                    packageId,
                    sourcePath,
                    sourceHash,
                    builder.Identity,
                    source,
                    cacheKey,
                    mipCount,
                    dimensions.GetBc3FileSize(mipCount),
                    finalDirectory,
                    finalPath));
                Interlocked.Increment(ref missCount);
            }

            int removedSources = index.RemoveMissingSources(packageId, retainedSourcePaths);
            Interlocked.Add(ref invalidatedCount, removedSources);
            RemoveStaleDirectories(packageCacheRoot, desiredDirectories);
            BuildMissingEntries(mod, files, missingEntries);
            currentCacheBytes = index.CurrentBytes;
        }

        private static void BuildMissingEntries(
            ModContentPack mod,
            Dictionary<string, FileInfo> files,
            List<TextureCacheEntry> missingEntries)
        {
            if (missingEntries.Count == 0)
            {
                return;
            }

            if (!builder.Available)
            {
                return;
            }

            List<TextureCacheEntry> entriesToBuild = SelectEntriesWithinBudget(missingEntries);
            Interlocked.Add(
                ref budgetSkippedCount,
                missingEntries.Count - entriesToBuild.Count);
            LoadingStageOperation operation = LoadingStageMailbox.Begin(
                Descriptor(
                    LoadingStage.Content,
                    LoadingStep.BuildTextureCache,
                    "Build texture cache",
                    "Converting " + entriesToBuild.Count + " textures for " + mod.Name,
                    LoadingModAttribution.Exact(mod),
                    mod.PackageId,
                    LoadingThreadAffinity.WorkerSafe));
            CacheBuildResult result;
            try
            {
                result = builder.Build(entriesToBuild);
                if (result.Failed > 0)
                {
                    operation.Fail();
                }
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

            Interlocked.Add(ref createdCount, result.Created);
            Interlocked.Add(ref failedCount, result.Failed);
            Interlocked.Add(ref buildMilliseconds, (long)Math.Round(result.Milliseconds));
            if (result.Error != null)
            {
                Log.Warning(
                    "[FixWorld] DDS cache build for " + mod.PackageId + ": " + result.Error);
            }

            foreach (TextureCacheEntry entry in entriesToBuild)
            {
                if (!File.Exists(entry.FinalPath))
                {
                    continue;
                }

                index.RegisterExisting(
                    entry.PackageId,
                    entry.SourcePath,
                    entry.Source,
                    entry.SourceHash,
                    entry.ConverterIdentity,
                    entry.FinalPath,
                    createdAfterOpen: true);
                files[entry.Key] = new FileInfo(entry.FinalPath);
            }
        }

        private static void PruneAndSave()
        {
            LoadingStageOperation pruneOperation = LoadingStageMailbox.Begin(
                Descriptor(
                    LoadingStage.Finalize,
                    LoadingStep.PruneTextureCache,
                    "Prune texture cache",
                    "Removing obsolete and over-budget DDS cache entries",
                    affinity: LoadingThreadAffinity.WorkerSafe));
            try
            {
                HashSet<string> activePackageIds = new HashSet<string>(
                    LoadedModManager.RunningModsListForReading
                        .Select(mod => Normalize(mod.PackageId)),
                    StringComparer.Ordinal);
                int removedEntries = index.RemoveInactivePackages(activePackageIds);
                int removedDirectories = RemoveInactivePackageDirectories(activePackageIds);
                int budgetRemovals = index.EnforceBudget(maxCacheBytes);
                Interlocked.Add(
                    ref invalidatedCount,
                    removedEntries + removedDirectories + budgetRemovals);
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

            LoadingStageOperation saveOperation = LoadingStageMailbox.Begin(
                Descriptor(
                    LoadingStage.Finalize,
                    LoadingStep.SaveTextureCacheIndex,
                    "Save texture cache index",
                    "Writing the DDS cache index atomically",
                    affinity: LoadingThreadAffinity.WorkerSafe));
            try
            {
                index.Save();
                currentCacheBytes = index.CurrentBytes;
            }
            catch
            {
                saveOperation.Fail();
                throw;
            }
            finally
            {
                saveOperation.Dispose();
            }
        }

        private static bool TryRecoverLegacyEntry(
            string packageId,
            string sourcePath,
            FileInfo source,
            string modRoot,
            string packageCacheRoot,
            out string cachePath)
        {
            cachePath = null;
            if (!index.RecoveryRequired)
            {
                return false;
            }

            string legacyHash = GetLegacyCacheHash(source, modRoot);
            string candidate = Path.Combine(
                packageCacheRoot,
                legacyHash,
                Path.GetFileNameWithoutExtension(source.Name) + ".dds");
            if (!File.Exists(candidate))
            {
                return false;
            }

            index.RegisterExisting(
                packageId,
                sourcePath,
                source,
                null,
                builder.Identity,
                candidate,
                createdAfterOpen: false);
            cachePath = candidate;
            return true;
        }

        private static List<TextureCacheEntry> SelectEntriesWithinBudget(
            List<TextureCacheEntry> entries)
        {
            if (!builder.Available)
            {
                return new List<TextureCacheEntry>();
            }

            List<TextureCacheEntry> selected = new List<TextureCacheEntry>(entries.Count);
            long availableFreeBytes = new DriveInfo(Path.GetPathRoot(cacheRoot)).AvailableFreeSpace;
            long projectedCacheBytes = index.CurrentBytes;
            long projectedTemporaryBytes = 0L;
            foreach (TextureCacheEntry entry in entries)
            {
                long entryTemporaryBytes = entry.Source.Length + entry.EstimatedCacheBytes;
                if ((maxCacheBytes > 0L &&
                     projectedCacheBytes + entry.EstimatedCacheBytes > maxCacheBytes) ||
                    availableFreeBytes - projectedTemporaryBytes - entryTemporaryBytes <
                    minimumFreeBytes)
                {
                    continue;
                }

                selected.Add(entry);
                projectedCacheBytes += entry.EstimatedCacheBytes;
                projectedTemporaryBytes += entryTemporaryBytes;
            }

            return selected;
        }

        private static void RemoveStaleDirectories(
            string packageCacheRoot,
            ISet<string> desiredDirectories)
        {
            if (!Directory.Exists(packageCacheRoot))
            {
                return;
            }

            foreach (string directory in Directory.EnumerateDirectories(packageCacheRoot))
            {
                string name = Path.GetFileName(directory);
                if (!IsCacheHash(name) || desiredDirectories.Contains(directory))
                {
                    continue;
                }

                EnsureChildPath(packageCacheRoot, directory);
                Directory.Delete(directory, true);
                Interlocked.Increment(ref invalidatedCount);
            }
        }

        private static int RemoveInactivePackageDirectories(ISet<string> activePackageIds)
        {
            HashSet<string> activeDirectories = new HashSet<string>(
                activePackageIds.Select(SanitizePathSegment),
                StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            foreach (string directory in Directory.EnumerateDirectories(cacheRoot))
            {
                string name = Path.GetFileName(directory);
                if (name.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase))
                {
                    RemoveOwnedDirectory(directory);
                    removed++;
                    continue;
                }

                if (activeDirectories.Contains(name) || !IsOwnedPackageDirectory(directory))
                {
                    continue;
                }

                RemoveOwnedDirectory(directory);
                removed++;
            }

            return removed;
        }

        private static bool IsOwnedPackageDirectory(string directory)
        {
            return Directory.EnumerateFileSystemEntries(directory).All(entry =>
            {
                if (!Directory.Exists(entry) || !IsCacheHash(Path.GetFileName(entry)))
                {
                    return false;
                }

                return Directory.EnumerateFileSystemEntries(entry).All(file =>
                    File.Exists(file) &&
                    string.Equals(
                        Path.GetExtension(file),
                        ".dds",
                        StringComparison.OrdinalIgnoreCase));
            });
        }

        private static void RemoveOwnedDirectory(string directory)
        {
            EnsureChildPath(cacheRoot, directory);
            Directory.Delete(directory, true);
        }

        private static void LogConfiguration()
        {
            string maxCacheDescription =
                ToGiB(maxCacheBytes).ToString("0.###", CultureInfo.InvariantCulture) + " GiB";
            if (builder.Available)
            {
                Log.Message(
                    "[FixWorld] DDS cache enabled at " + cacheRoot +
                    "; index=" + index.LoadStatus +
                    "; entries=" + index.EntryCount +
                    "; texconv=" + builder.TexconvPath +
                    "; maxCache=" + maxCacheDescription +
                    "; minFreeGiB=" +
                    ToGiB(minimumFreeBytes).ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }

            Log.Warning(
                "[FixWorld] DDS cache can read existing entries, but texconv was not found; " +
                "missing or changed entries will use their original textures. " +
                "Index=" + index.LoadStatus + ".");
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
                CacheIdentityVersion + "\n" + sourcePath + "\n" + sourceHash + "\n" +
                (converterIdentity ?? "unavailable"));
        }

        private static string GetLegacyCacheHash(FileInfo source, string modRoot)
        {
            string relativePath = GetRelativeSourcePath(source, modRoot);
            return GetTextHash(
                LegacyCacheIdentityVersion + "\n" +
                relativePath + "\n" +
                source.Length.ToString(CultureInfo.InvariantCulture) + "\n" +
                source.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
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

        private static bool IsCacheHash(string value)
        {
            return value.Length == 64 && value.All(character =>
                character >= '0' && character <= '9' ||
                character >= 'a' && character <= 'f');
        }

        private static void EnsureChildPath(string parent, string child)
        {
            string resolvedParent = Path.GetFullPath(parent)
                                        .TrimEnd(
                                            Path.DirectorySeparatorChar,
                                            Path.AltDirectorySeparatorChar) +
                                    Path.DirectorySeparatorChar;
            string resolvedChild = Path.GetFullPath(child);
            if (!resolvedChild.StartsWith(resolvedParent, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid cache path: " + resolvedChild);
            }
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
            long maxCacheBytes)
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
        }
    }
}
