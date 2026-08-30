using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;

namespace RimWorldOptim.Poc.Caching
{
    internal static class TextureDdsCache
    {
        private const string CacheIdentityVersion = "bc3-unorm-mips-v1";
        private const string EnabledEnvironmentVariable = "RIMWORLDOPTIM_DDS_CACHE";
        private const string CacheRootEnvironmentVariable = "RIMWORLDOPTIM_DDS_CACHE_ROOT";
        private const string MaxCacheGiBEnvironmentVariable = "RIMWORLDOPTIM_DDS_CACHE_MAX_GIB";
        private const string MinimumFreeGiBEnvironmentVariable = "RIMWORLDOPTIM_DDS_CACHE_MIN_FREE_GIB";
        private const long DefaultMaxCacheBytes = 4L * 1024L * 1024L * 1024L;
        private const long DefaultMinimumFreeBytes = 5L * 1024L * 1024L * 1024L;

        private static readonly object Sync = new object();

        private static bool initialized;
        private static bool enabled;
        private static string cacheRoot;
        private static TextureDdsCacheBuilder builder;
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

        internal static void Initialize(string modRoot)
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
                Log.Message("[RimWorldOptim.Poc] DDS cache disabled.");
                return;
            }

            try
            {
                cacheRoot = Environment.GetEnvironmentVariable(CacheRootEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(cacheRoot))
                {
                    cacheRoot = Path.Combine(
                        GenFilePaths.SaveDataFolderPath,
                        "RimWorldOptim",
                        "TextureCache",
                        "dds-v1");
                }

                cacheRoot = Path.GetFullPath(cacheRoot);
                Directory.CreateDirectory(cacheRoot);
                maxCacheBytes = ReadGiBLimit(MaxCacheGiBEnvironmentVariable, DefaultMaxCacheBytes);
                minimumFreeBytes = ReadGiBLimit(MinimumFreeGiBEnvironmentVariable, DefaultMinimumFreeBytes);
                currentCacheBytes = GetDirectorySize(cacheRoot);
                builder = new TextureDdsCacheBuilder(cacheRoot, modRoot);
                if (builder.Available)
                {
                    Log.Message(
                        "[RimWorldOptim.Poc] DDS cache enabled at " + cacheRoot +
                        "; texconv=" + builder.TexconvPath +
                        "; maxGiB=" + ToGiB(maxCacheBytes).ToString("0.###", CultureInfo.InvariantCulture) +
                        "; minFreeGiB=" + ToGiB(minimumFreeBytes).ToString("0.###", CultureInfo.InvariantCulture));
                }
                else
                {
                    Log.Warning(
                        "[RimWorldOptim.Poc] DDS cache can read existing entries, but texconv.exe was not found; " +
                        "missing or changed entries will use their original textures.");
                }
            }
            catch (Exception exception)
            {
                enabled = false;
                Log.Warning("[RimWorldOptim.Poc] DDS cache disabled after initialization failure: " + exception);
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
                !string.Equals(contentPath, GenFilePaths.ContentPath<Texture2D>(), StringComparison.Ordinal))
            {
                return;
            }

            lock (Sync)
            {
                try
                {
                    ApplyCore(mod, files);
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[RimWorldOptim.Poc] DDS cache skipped for " + mod.PackageId + ": " + exception);
                }
            }
        }

        internal static void WriteSummary()
        {
            if (!enabled)
            {
                return;
            }

            Log.Message(string.Format(
                CultureInfo.InvariantCulture,
                "[RimWorldOptim.Poc] DDS cache profile: hits={0}; misses={1}",
                Interlocked.Read(ref hitCount),
                Interlocked.Read(ref missCount)));
            Log.Message(string.Format(
                CultureInfo.InvariantCulture,
                "[RimWorldOptim.Poc] DDS cache build: created={0}; invalidated={1}; excluded={2}; unsupported={3}; budgetSkipped={4}; failed={5}; buildMs={6}; cacheBytes={7}; maxCacheBytes={8}",
                Interlocked.Read(ref createdCount),
                Interlocked.Read(ref invalidatedCount),
                Interlocked.Read(ref excludedCount),
                Interlocked.Read(ref unsupportedCount),
                Interlocked.Read(ref budgetSkippedCount),
                Interlocked.Read(ref failedCount),
                Interlocked.Read(ref buildMilliseconds),
                Interlocked.Read(ref currentCacheBytes),
                maxCacheBytes));
        }

        private static void ApplyCore(ModContentPack mod, Dictionary<string, FileInfo> files)
        {
            string packageCacheRoot = Path.Combine(cacheRoot, SanitizePathSegment(mod.PackageId));
            string modRoot = Path.GetFullPath(mod.RootDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
            HashSet<string> shippedDdsPaths = new HashSet<string>(
                files.Keys
                    .Select(path => path.Replace('\\', '/').ToLowerInvariant())
                    .Where(path => path.EndsWith(".dds", StringComparison.Ordinal)),
                StringComparer.Ordinal);
            HashSet<string> desiredHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<TextureCacheEntry> missingEntries = new List<TextureCacheEntry>();

            foreach (KeyValuePair<string, FileInfo> item in files.ToList())
            {
                FileInfo source = item.Value;
                string sourceKey = item.Key.Replace('\\', '/').ToLowerInvariant();
                string extension = source.Extension.ToLowerInvariant();
                if (extension == ".dds" ||
                    shippedDdsPaths.Contains(Path.ChangeExtension(sourceKey, ".dds")) ||
                    !source.FullName.StartsWith(modRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" ||
                    !TextureDimensions.TryRead(source, out TextureDimensions dimensions))
                {
                    Interlocked.Increment(ref unsupportedCount);
                    continue;
                }

                int mipCount = dimensions.GetBc3MipCount();
                if (mipCount == 0)
                {
                    Interlocked.Increment(ref excludedCount);
                    continue;
                }

                string hash = GetCacheHash(source, modRoot);
                desiredHashes.Add(hash);
                string finalDirectory = Path.Combine(packageCacheRoot, hash);
                string finalPath = Path.Combine(
                    finalDirectory,
                    Path.GetFileNameWithoutExtension(source.Name) + ".dds");
                if (File.Exists(finalPath))
                {
                    files[item.Key] = new FileInfo(finalPath);
                    Interlocked.Increment(ref hitCount);
                }
                else
                {
                    missingEntries.Add(new TextureCacheEntry(
                        item.Key,
                        source,
                        hash,
                        mipCount,
                        dimensions.GetBc3FileSize(mipCount),
                        finalDirectory,
                        finalPath));
                    Interlocked.Increment(ref missCount);
                }
            }

            if (Directory.Exists(packageCacheRoot))
            {
                CacheCleanupResult cleanup = RemoveStaleDirectories(packageCacheRoot, desiredHashes);
                Interlocked.Add(ref invalidatedCount, cleanup.RemovedDirectories);
                currentCacheBytes = Math.Max(0L, currentCacheBytes - cleanup.RemovedBytes);
            }

            List<TextureCacheEntry> entriesToBuild = SelectEntriesWithinBudget(missingEntries);
            Interlocked.Add(ref budgetSkippedCount, missingEntries.Count - entriesToBuild.Count);
            CacheBuildResult result = builder.Build(entriesToBuild);
            Interlocked.Add(ref createdCount, result.Created);
            Interlocked.Add(ref failedCount, result.Failed);
            Interlocked.Add(ref buildMilliseconds, (long)Math.Round(result.Milliseconds));
            currentCacheBytes += result.CreatedBytes;
            if (result.Error != null)
            {
                Log.Warning("[RimWorldOptim.Poc] DDS cache build for " + mod.PackageId + ": " + result.Error);
            }

            foreach (TextureCacheEntry entry in missingEntries)
            {
                if (File.Exists(entry.FinalPath))
                {
                    files[entry.Key] = new FileInfo(entry.FinalPath);
                }
            }

        }

        private static List<TextureCacheEntry> SelectEntriesWithinBudget(List<TextureCacheEntry> entries)
        {
            List<TextureCacheEntry> selected = new List<TextureCacheEntry>(entries.Count);
            long availableFreeBytes = new DriveInfo(Path.GetPathRoot(cacheRoot)).AvailableFreeSpace;
            long projectedCacheBytes = currentCacheBytes;
            long projectedTemporaryBytes = 0L;
            foreach (TextureCacheEntry entry in entries)
            {
                long entryTemporaryBytes = entry.Source.Length + entry.EstimatedCacheBytes;
                if (projectedCacheBytes + entry.EstimatedCacheBytes > maxCacheBytes ||
                    availableFreeBytes - projectedTemporaryBytes - entryTemporaryBytes < minimumFreeBytes)
                {
                    continue;
                }

                selected.Add(entry);
                projectedCacheBytes += entry.EstimatedCacheBytes;
                projectedTemporaryBytes += entryTemporaryBytes;
            }

            return selected;
        }

        private static CacheCleanupResult RemoveStaleDirectories(
            string packageCacheRoot,
            HashSet<string> desiredHashes)
        {
            int removed = 0;
            long removedBytes = 0L;
            foreach (string directory in Directory.EnumerateDirectories(packageCacheRoot))
            {
                string name = Path.GetFileName(directory);
                if (!IsCacheHash(name) || desiredHashes.Contains(name))
                {
                    continue;
                }

                EnsureChildPath(packageCacheRoot, directory);
                removedBytes += GetDirectorySize(directory);
                Directory.Delete(directory, true);
                removed++;
            }

            return new CacheCleanupResult(removed, removedBytes);
        }

        private static long ReadGiBLimit(string environmentVariable, long defaultBytes)
        {
            string value = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultBytes;
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gibibytes) ||
                gibibytes <= 0.0 || gibibytes > long.MaxValue / (1024.0 * 1024.0 * 1024.0))
            {
                throw new InvalidOperationException(
                    "Invalid positive GiB value in " + environmentVariable + ": " + value);
            }

            return (long)Math.Floor(gibibytes * 1024.0 * 1024.0 * 1024.0);
        }

        private static long GetDirectorySize(string directory)
        {
            long bytes = 0L;
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                bytes += new FileInfo(file).Length;
            }

            return bytes;
        }

        private static double ToGiB(long bytes)
        {
            return bytes / (1024.0 * 1024.0 * 1024.0);
        }

        private static string GetCacheHash(FileInfo source, string modRoot)
        {
            string relativePath = source.FullName.Substring(modRoot.Length)
                .Replace('\\', '/')
                .ToLowerInvariant();
            string identity = CacheIdentityVersion + "\n" +
                              relativePath + "\n" +
                              source.Length.ToString(CultureInfo.InvariantCulture) + "\n" +
                              source.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(identity));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string SanitizePathSegment(string value)
        {
            HashSet<char> invalidCharacters = new HashSet<char>(Path.GetInvalidFileNameChars());
            return new string(value.ToLowerInvariant()
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray());
        }

        private static bool IsCacheHash(string value)
        {
            return value.Length == 64 && value.All(character =>
                character >= '0' && character <= '9' || character >= 'a' && character <= 'f');
        }

        private static void EnsureChildPath(string parent, string child)
        {
            string resolvedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) +
                                    Path.DirectorySeparatorChar;
            string resolvedChild = Path.GetFullPath(child);
            if (!resolvedChild.StartsWith(resolvedParent, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid cache path: " + resolvedChild);
            }
        }

        private readonly struct CacheCleanupResult
        {
            internal readonly int RemovedDirectories;
            internal readonly long RemovedBytes;

            internal CacheCleanupResult(int removedDirectories, long removedBytes)
            {
                RemovedDirectories = removedDirectories;
                RemovedBytes = removedBytes;
            }
        }
    }
}
