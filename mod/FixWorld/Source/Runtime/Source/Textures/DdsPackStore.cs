using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using FixWorld.Caching;
using FixWorld.Runtime;

namespace FixWorld.Textures
{
    internal sealed class DdsPackStore : IDisposable
    {
        private static readonly long AccessUpdateIntervalTicks =
            TimeSpan.FromHours(12).Ticks;

        private readonly string cacheRoot;
        private readonly string indexPath;
        private readonly string backupPath;
        private readonly string cacheIdentity;
        private readonly CacheWriter<string, DdsPackArtifact, DdsSourceStamp>
            writer;
        private readonly FileStream writerLock;
        private bool dirty;
        private long currentBytes;

        private DdsPackStore(
            string cacheRoot,
            string cacheIdentity,
            IDictionary<string, CacheEntry<DdsPackArtifact, DdsSourceStamp>>
                entries,
            FileStream writerLock,
            string loadStatus)
        {
            this.cacheRoot = cacheRoot;
            this.cacheIdentity = cacheIdentity;
            SnapshotCache<string, DdsPackArtifact, DdsSourceStamp> cache =
                new SnapshotCache<string, DdsPackArtifact, DdsSourceStamp>(
                    entries,
                    StringComparer.Ordinal);
            writer = cache.Writer;
            this.writerLock = writerLock;
            indexPath = Path.Combine(cacheRoot, DdsCacheContract.IndexFileName);
            backupPath = Path.Combine(cacheRoot, DdsCacheContract.BackupFileName);
            currentBytes = GetPackSize(cacheRoot);
            LoadStatus = loadStatus;
        }

        internal string CacheRoot => cacheRoot;

        internal long CurrentBytes => currentBytes;

        internal int EntryCount => writer.Count;

        internal string LoadStatus { get; }

        internal static DdsPackStore Open(
            string cacheRoot,
            string cacheIdentity)
        {
            string root = Path.GetFullPath(cacheRoot);
            Directory.CreateDirectory(root);
            FileStream writerLock = new FileStream(
                Path.Combine(root, DdsCacheContract.LockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            try
            {
                string index = Path.Combine(root, DdsCacheContract.IndexFileName);
                string backup = Path.Combine(root, DdsCacheContract.BackupFileName);
                TextureCacheManifest manifest = TryRead(index);
                if (!IsCompatible(manifest, cacheIdentity))
                {
                    manifest = TryRead(backup);
                }

                bool loaded = IsCompatible(manifest, cacheIdentity);
                return new DdsPackStore(
                    root,
                    cacheIdentity,
                    loaded
                        ? LoadEntries(root, manifest.Entries)
                        : new Dictionary<
                            string,
                            CacheEntry<DdsPackArtifact, DdsSourceStamp>>(
                            StringComparer.Ordinal),
                    writerLock,
                    loaded ? "loaded" : "new pack index");
            }
            catch
            {
                writerLock.Dispose();
                throw;
            }
        }

        internal DdsPackSnapshot Snapshot()
        {
            return new DdsPackSnapshot(cacheRoot, writer.Publish());
        }

        internal string CreateStagingRoot(string packageId)
        {
            string root = Path.Combine(
                cacheRoot,
                ".staging-" +
                DdsCacheKey.HashText(packageId).Substring(0, 12) + "-" +
                Guid.NewGuid().ToString("N"));
            DdsCacheKey.EnsureChildPath(cacheRoot, root);
            Directory.CreateDirectory(root);
            return root;
        }

        internal void TouchPackages(ISet<string> packageIds)
        {
            if (packageIds == null)
            {
                throw new ArgumentNullException(nameof(packageIds));
            }

            long now = DateTime.UtcNow.Ticks;
            long cutoff = now - AccessUpdateIntervalTicks;
            bool changed = false;
            foreach (KeyValuePair<
                         string,
                         CacheEntry<DdsPackArtifact, DdsSourceStamp>> pair in
                     writer.SnapshotEntries())
            {
                DdsPackArtifact artifact = pair.Value.Value;
                if (!packageIds.Contains(artifact.PackageId) ||
                    artifact.LastUsedUtcTicks > cutoff)
                {
                    continue;
                }

                writer.Upsert(
                    pair.Key,
                    artifact.WithLastUsed(now),
                    pair.Value.Stamp);
                changed = true;
            }

            dirty |= changed;
        }

        internal int ReconcilePackages(
            IDictionary<string, HashSet<string>> retainedByPackage)
        {
            string[] keys = writer.SnapshotEntries()
                .Where(pair => retainedByPackage.TryGetValue(
                                   pair.Value.Value.PackageId,
                                   out HashSet<string> retained) &&
                               !retained.Contains(
                                   pair.Value.Value.SourcePath))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (string key in keys)
            {
                writer.Remove(key);
            }

            if (keys.Length > 0)
            {
                dirty = true;
            }

            return keys.Length;
        }

        internal int RemoveInactivePackages(ISet<string> activePackages)
        {
            string[] keys = writer.SnapshotEntries()
                .Where(pair =>
                    !activePackages.Contains(pair.Value.Value.PackageId))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (string key in keys)
            {
                writer.Remove(key);
            }

            if (keys.Length > 0)
            {
                dirty = true;
            }

            return keys.Length;
        }

        internal void Publish(DdsBuiltPack pack)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            string finalDirectory = Path.Combine(
                cacheRoot,
                "packs",
                DdsCacheKey.Sanitize(pack.PackageId));
            string finalPath = Path.Combine(
                finalDirectory,
                pack.Generation + DdsCacheContract.PackFileExtension);
            DdsCacheKey.EnsureChildPath(cacheRoot, finalPath);
            Directory.CreateDirectory(finalDirectory);
            if (File.Exists(finalPath))
            {
                File.Delete(pack.TemporaryPath);
            }
            else
            {
                File.Move(pack.TemporaryPath, finalPath);
            }

            string relativePath = GetRelativePackPath(finalPath);
            string normalizedPackage = DdsCacheKey.Normalize(pack.PackageId);
            foreach (string key in writer.SnapshotEntries()
                         .Where(pair => string.Equals(
                             pair.Value.Value.PackageId,
                             normalizedPackage,
                             StringComparison.Ordinal))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                writer.Remove(key);
            }

            long now = DateTime.UtcNow.Ticks;
            foreach (DdsBuiltEntry entry in pack.Entries)
            {
                string sourcePath = DdsCacheKey.Normalize(entry.SourcePath);
                writer.Upsert(
                    DdsCacheKey.Entry(normalizedPackage, sourcePath),
                    new DdsPackArtifact(
                        normalizedPackage,
                        sourcePath,
                        relativePath,
                        entry.Offset,
                        entry.Length,
                        now),
                    new DdsSourceStamp(
                        entry.Source.Length,
                        entry.Source.LastWriteTimeUtc.Ticks,
                        entry.SourceHash,
                        entry.ConverterIdentity));
            }

            dirty = true;
            currentBytes = GetPackSize(cacheRoot);
            Save();
            SweepOrphans();
        }

        internal void Discard(DdsBuiltPack pack)
        {
            if (pack != null)
            {
                TryDeleteDirectory(pack.StagingRoot);
            }
        }

        internal void DiscardStaging(string stagingRoot)
        {
            if (string.IsNullOrWhiteSpace(stagingRoot))
            {
                return;
            }

            DdsCacheKey.EnsureChildPath(cacheRoot, stagingRoot);
            TryDeleteDirectory(stagingRoot);
        }

        internal int EnforceBudget(long maximumBytes)
        {
            if (maximumBytes <= 0L || currentBytes <= maximumBytes)
            {
                return 0;
            }

            int removed = 0;
            foreach (IGrouping<string, KeyValuePair<
                         string,
                         CacheEntry<DdsPackArtifact, DdsSourceStamp>>> pack in
                     writer.SnapshotEntries()
                         .GroupBy(
                             pair => pair.Value.Value.PackPath,
                             StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Min(pair =>
                             pair.Value.Value.LastUsedUtcTicks))
                         .ToArray())
            {
                foreach (KeyValuePair<
                             string,
                             CacheEntry<DdsPackArtifact, DdsSourceStamp>> entry in pack)
                {
                    writer.Remove(entry.Key);
                    removed++;
                }

                dirty = true;
                Save();
                SweepOrphans();
                if (currentBytes <= maximumBytes)
                {
                    break;
                }
            }

            return removed;
        }

        internal int SweepOrphans()
        {
            HashSet<string> retained = new HashSet<string>(
                writer.SnapshotEntries()
                    .Select(pair => pair.Value.Value.PackPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(path => TryResolvePackPath(
                        cacheRoot,
                        path,
                        out string resolved)
                        ? resolved
                        : null)
                    .Where(path => path != null),
                StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            foreach (string path in Directory.EnumerateFiles(
                         cacheRoot,
                         "*" + DdsCacheContract.PackFileExtension,
                         SearchOption.AllDirectories))
            {
                string resolved = Path.GetFullPath(path);
                if (!retained.Contains(resolved))
                {
                    File.Delete(resolved);
                    removed++;
                }
            }

            foreach (string directory in Directory.EnumerateDirectories(
                         cacheRoot,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length)
                     .ToArray())
            {
                if (Path.GetFileName(directory).StartsWith(
                        ".staging-",
                        StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteDirectory(directory);
                }
                else if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            currentBytes = GetPackSize(cacheRoot);
            return removed;
        }

        internal void Save()
        {
            writer.Publish();
            if (!dirty && File.Exists(indexPath))
            {
                return;
            }

            currentBytes = GetPackSize(cacheRoot);
            TextureCacheManifest manifest = new TextureCacheManifest
            {
                SchemaVersion = DdsCacheContract.ManifestSchemaVersion,
                CacheIdentity = cacheIdentity,
                WrittenUtcTicks = DateTime.UtcNow.Ticks,
                TotalBytes = currentBytes,
                Entries = writer.SnapshotEntries()
                    .Select(ToManifestEntry)
                    .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.SourcePath, StringComparer.Ordinal)
                    .ToList()
            };
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(TextureCacheManifest));
            AtomicFile.Write(
                indexPath,
                stream => serializer.WriteObject(stream, manifest),
                backupPath);
            dirty = false;
        }

        public void Dispose()
        {
            writerLock.Dispose();
        }

        internal static bool ConverterMatches(
            string indexed,
            string converterIdentity)
        {
            return string.IsNullOrEmpty(converterIdentity) ||
                   string.Equals(
                       indexed,
                       converterIdentity,
                       StringComparison.Ordinal);
        }

        internal static bool TryResolvePackPath(
            string cacheRoot,
            string relativePath,
            out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath) ||
                !string.Equals(
                    Path.GetExtension(relativePath),
                    DdsCacheContract.PackFileExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string root = Path.GetFullPath(cacheRoot).TrimEnd(
                              Path.DirectorySeparatorChar,
                              Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(
                cacheRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            resolvedPath = candidate;
            return true;
        }

        private string GetRelativePackPath(string path)
        {
            string root = cacheRoot.TrimEnd(
                              Path.DirectorySeparatorChar,
                              Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Pack is outside the DDS cache: " + resolved);
            }

            return resolved.Substring(root.Length).Replace('\\', '/');
        }

        private static Dictionary<
            string,
            CacheEntry<DdsPackArtifact, DdsSourceStamp>> LoadEntries(
            string cacheRoot,
            IEnumerable<TextureCacheManifestEntry> entries)
        {
            Dictionary<string, CacheEntry<DdsPackArtifact, DdsSourceStamp>>
                result = new Dictionary<
                    string,
                    CacheEntry<DdsPackArtifact, DdsSourceStamp>>(
                    StringComparer.Ordinal);
            if (entries == null)
            {
                return result;
            }

            foreach (TextureCacheManifestEntry entry in entries)
            {
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.PackageId) ||
                    string.IsNullOrWhiteSpace(entry.SourcePath) ||
                    entry.CacheOffset < 0L ||
                    entry.CacheBytes <= 0L ||
                    !TryResolvePackPath(
                        cacheRoot,
                        entry.CachePath,
                        out string packPath))
                {
                    continue;
                }

                FileInfo pack = new FileInfo(packPath);
                if (!pack.Exists ||
                    entry.CacheOffset > pack.Length - entry.CacheBytes)
                {
                    continue;
                }

                string packageId = DdsCacheKey.Normalize(entry.PackageId);
                string sourcePath = DdsCacheKey.Normalize(entry.SourcePath);
                result[DdsCacheKey.Entry(packageId, sourcePath)] =
                    new CacheEntry<DdsPackArtifact, DdsSourceStamp>(
                        new DdsPackArtifact(
                            packageId,
                            sourcePath,
                            entry.CachePath,
                            entry.CacheOffset,
                            entry.CacheBytes,
                            entry.LastUsedUtcTicks),
                        new DdsSourceStamp(
                            entry.SourceLength,
                            entry.SourceWriteTimeUtcTicks,
                            entry.SourceHash,
                            entry.ConverterIdentity));
            }

            return result;
        }

        private static TextureCacheManifestEntry ToManifestEntry(
            KeyValuePair<
                string,
                CacheEntry<DdsPackArtifact, DdsSourceStamp>> pair)
        {
            DdsPackArtifact artifact = pair.Value.Value;
            DdsSourceStamp stamp = pair.Value.Stamp;
            return new TextureCacheManifestEntry
            {
                PackageId = artifact.PackageId,
                SourcePath = artifact.SourcePath,
                SourceLength = stamp.SourceLength,
                SourceWriteTimeUtcTicks = stamp.SourceWriteTimeUtcTicks,
                SourceHash = stamp.SourceHash,
                ConverterIdentity = stamp.ConverterIdentity,
                CachePath = artifact.PackPath,
                CacheOffset = artifact.Offset,
                CacheBytes = artifact.Length,
                LastUsedUtcTicks = artifact.LastUsedUtcTicks
            };
        }

        private static TextureCacheManifest TryRead(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(TextureCacheManifest));
                if (DdsCacheContract.TryGetPublishedIndex(path, out byte[] bytes))
                {
                    using (MemoryStream stream = new MemoryStream(bytes, false))
                    {
                        return serializer.ReadObject(stream) as
                            TextureCacheManifest;
                    }
                }

                using (FileStream stream = File.OpenRead(path))
                {
                    return serializer.ReadObject(stream) as TextureCacheManifest;
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (SerializationException)
            {
                return null;
            }
        }

        private static bool IsCompatible(
            TextureCacheManifest manifest,
            string cacheIdentity)
        {
            return manifest != null &&
                   manifest.SchemaVersion ==
                   DdsCacheContract.ManifestSchemaVersion &&
                   string.Equals(
                       manifest.CacheIdentity,
                       cacheIdentity,
                       StringComparison.Ordinal);
        }

        private static long GetPackSize(string cacheRoot)
        {
            return Directory.EnumerateFiles(
                    cacheRoot,
                    "*" + DdsCacheContract.PackFileExtension,
                    SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal sealed class DdsPackSnapshot
    {
        private readonly string cacheRoot;
        private readonly CacheSnapshot<string, DdsPackArtifact, DdsSourceStamp>
            snapshot;

        internal DdsPackSnapshot(
            string cacheRoot,
            CacheSnapshot<string, DdsPackArtifact, DdsSourceStamp> snapshot)
        {
            this.cacheRoot = cacheRoot;
            this.snapshot = snapshot;
        }

        internal bool TryGetFresh(
            string packageId,
            string sourcePath,
            FileInfo source,
            string converterIdentity,
            out DdsPackSlice slice)
        {
            slice = default;
            return snapshot.TryGet(
                       DdsCacheKey.Entry(packageId, sourcePath),
                       out CacheEntry<DdsPackArtifact, DdsSourceStamp> entry) &&
                   entry.Stamp.SourceLength == source.Length &&
                   entry.Stamp.SourceWriteTimeUtcTicks ==
                   source.LastWriteTimeUtc.Ticks &&
                   DdsPackStore.ConverterMatches(
                       entry.Stamp.ConverterIdentity,
                       converterIdentity) &&
                   TryResolve(entry.Value, out slice);
        }

        private bool TryResolve(
            DdsPackArtifact artifact,
            out DdsPackSlice slice)
        {
            slice = default;
            if (!DdsPackStore.TryResolvePackPath(
                    cacheRoot,
                    artifact.PackPath,
                    out string path))
            {
                return false;
            }

            FileInfo pack = new FileInfo(path);
            if (!pack.Exists ||
                artifact.Offset < 0L ||
                artifact.Length <= 0L ||
                artifact.Offset > pack.Length - artifact.Length)
            {
                return false;
            }

            slice = new DdsPackSlice(path, artifact.Offset, artifact.Length);
            return true;
        }
    }

    internal readonly struct DdsPackSlice
    {
        internal DdsPackSlice(string path, long offset, long length)
        {
            Path = path;
            Offset = offset;
            Length = length;
        }

        internal string Path { get; }

        internal long Offset { get; }

        internal long Length { get; }
    }

    internal readonly struct DdsSourceStamp
    {
        internal DdsSourceStamp(
            long sourceLength,
            long sourceWriteTimeUtcTicks,
            string sourceHash,
            string converterIdentity)
        {
            SourceLength = sourceLength;
            SourceWriteTimeUtcTicks = sourceWriteTimeUtcTicks;
            SourceHash = sourceHash;
            ConverterIdentity = converterIdentity;
        }

        internal long SourceLength { get; }
        internal long SourceWriteTimeUtcTicks { get; }
        internal string SourceHash { get; }
        internal string ConverterIdentity { get; }
    }

    internal readonly struct DdsPackArtifact
    {
        internal DdsPackArtifact(
            string packageId,
            string sourcePath,
            string packPath,
            long offset,
            long length,
            long lastUsedUtcTicks)
        {
            PackageId = packageId;
            SourcePath = sourcePath;
            PackPath = packPath;
            Offset = offset;
            Length = length;
            LastUsedUtcTicks = lastUsedUtcTicks;
        }

        internal string PackageId { get; }
        internal string SourcePath { get; }
        internal string PackPath { get; }
        internal long Offset { get; }
        internal long Length { get; }
        internal long LastUsedUtcTicks { get; }

        internal DdsPackArtifact WithLastUsed(long ticks)
        {
            return new DdsPackArtifact(
                PackageId,
                SourcePath,
                PackPath,
                Offset,
                Length,
                ticks);
        }
    }

    internal sealed class DdsBuiltPack
    {
        internal DdsBuiltPack(
            string packageId,
            string generation,
            string stagingRoot,
            string temporaryPath,
            IReadOnlyList<DdsBuiltEntry> entries)
        {
            PackageId = packageId;
            Generation = generation;
            StagingRoot = stagingRoot;
            TemporaryPath = temporaryPath;
            Entries = entries;
        }

        internal string PackageId { get; }
        internal string Generation { get; }
        internal string StagingRoot { get; }
        internal string TemporaryPath { get; }
        internal IReadOnlyList<DdsBuiltEntry> Entries { get; }
    }

    internal readonly struct DdsBuiltEntry
    {
        internal DdsBuiltEntry(
            string sourcePath,
            FileInfo source,
            string sourceHash,
            string converterIdentity,
            long offset,
            long length)
        {
            SourcePath = sourcePath;
            Source = source;
            SourceHash = sourceHash;
            ConverterIdentity = converterIdentity;
            Offset = offset;
            Length = length;
        }

        internal string SourcePath { get; }
        internal FileInfo Source { get; }
        internal string SourceHash { get; }
        internal string ConverterIdentity { get; }
        internal long Offset { get; }
        internal long Length { get; }
    }

    internal static class DdsCacheKey
    {
        internal static string Normalize(string value)
        {
            return value.Replace('\\', '/').ToLowerInvariant();
        }

        internal static string Entry(string packageId, string sourcePath)
        {
            return Normalize(packageId) + "\n" + Normalize(sourcePath);
        }

        internal static string RelativeSource(FileInfo source, string modRoot)
        {
            return Normalize(source.FullName.Substring(modRoot.Length));
        }

        internal static string HashFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return Hex(hash.ComputeHash(stream));
            }
        }

        internal static string HashText(string value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        internal static string Sanitize(string value)
        {
            HashSet<char> invalid = new HashSet<char>(
                Path.GetInvalidFileNameChars());
            return new string(Normalize(value)
                .Select(character => invalid.Contains(character) ? '_' : character)
                .ToArray());
        }

        internal static void EnsureChildPath(string parent, string child)
        {
            string root = Path.GetFullPath(parent).TrimEnd(
                              Path.DirectorySeparatorChar,
                              Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(child);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Invalid cache path: " + path);
            }
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }
    }
}
