using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using FixWorld.Caching;
using FixWorld.Runtime;

namespace FixWorld.Textures
{
    internal sealed class TextureCacheStore : IDisposable
    {
        private readonly string cacheRoot;
        private readonly string indexPath;
        private readonly string backupPath;
        private readonly string cacheIdentity;
        private readonly CacheWriter<
            string,
            TextureCacheArtifact,
            TextureCacheFingerprint> writer;
        private readonly FileStream writerLock;

        private bool dirty;
        private bool recoveryRequired;
        private long currentBytes;

        private TextureCacheStore(
            string cacheRoot,
            string cacheIdentity,
            IDictionary<
                string,
                CacheEntry<TextureCacheArtifact, TextureCacheFingerprint>> entries,
            long currentBytes,
            bool recoveryRequired,
            FileStream writerLock,
            string loadStatus)
        {
            this.cacheRoot = cacheRoot;
            this.cacheIdentity = cacheIdentity;
            SnapshotCache<
                string,
                TextureCacheArtifact,
                TextureCacheFingerprint> cache = new SnapshotCache<
                    string,
                    TextureCacheArtifact,
                    TextureCacheFingerprint>(entries, StringComparer.Ordinal);
            writer = cache.Writer;
            this.currentBytes = currentBytes;
            this.recoveryRequired = recoveryRequired;
            this.writerLock = writerLock;
            indexPath = Path.Combine(cacheRoot, DdsCacheContract.IndexFileName);
            backupPath = Path.Combine(cacheRoot, DdsCacheContract.BackupFileName);
            LoadStatus = loadStatus;
        }

        internal long CurrentBytes => currentBytes;

        internal int EntryCount => writer.Count;

        internal string LoadStatus { get; }

        internal static TextureCacheStore Open(string cacheRoot, string cacheIdentity)
        {
            string resolvedRoot = Path.GetFullPath(cacheRoot);
            Directory.CreateDirectory(resolvedRoot);
            string lockPath = Path.Combine(resolvedRoot, DdsCacheContract.LockFileName);
            FileStream writerLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            try
            {
                string indexPath = Path.Combine(
                    resolvedRoot,
                    DdsCacheContract.IndexFileName);
                string backupPath = Path.Combine(
                    resolvedRoot,
                    DdsCacheContract.BackupFileName);
                TextureCacheManifest document = TryRead(indexPath);
                if (!IsCompatible(document, cacheIdentity))
                {
                    document = TryRead(backupPath);
                }

                if (IsCompatible(document, cacheIdentity))
                {
                    Dictionary<
                        string,
                        CacheEntry<TextureCacheArtifact, TextureCacheFingerprint>>
                        loadedEntries =
                        LoadEntries(resolvedRoot, document.Entries);
                    long bytes = loadedEntries.Values.Sum(entry =>
                        Math.Max(0L, entry.Value.CacheBytes));
                    return new TextureCacheStore(
                        resolvedRoot,
                        cacheIdentity,
                        loadedEntries,
                        bytes,
                        recoveryRequired: false,
                        writerLock,
                        "loaded");
                }

                long recoveredBytes = GetDdsSize(resolvedRoot);
                string status = File.Exists(indexPath) || File.Exists(backupPath)
                    ? "rebuilding incompatible or damaged index"
                    : recoveredBytes > 0L
                        ? "rebuilding missing index"
                        : "new index";
                return new TextureCacheStore(
                    resolvedRoot,
                    cacheIdentity,
                    new Dictionary<
                        string,
                        CacheEntry<TextureCacheArtifact, TextureCacheFingerprint>>(
                        StringComparer.Ordinal),
                    recoveredBytes,
                    recoveryRequired: recoveredBytes > 0L,
                    writerLock,
                    status);
            }
            catch
            {
                writerLock.Dispose();
                throw;
            }
        }


        internal TextureCacheSnapshot CreateValidationSnapshot()
        {
            return new TextureCacheSnapshot(cacheRoot, writer.Publish());
        }

        internal void TouchPrepared(string packageId, string sourcePath)
        {
            string key = TextureCacheIdentity.GetEntryKey(packageId, sourcePath);
            if (!writer.TryGet(
                    key,
                    out CacheEntry<TextureCacheArtifact, TextureCacheFingerprint> entry))
            {
                throw new InvalidOperationException(
                    "Prepared DDS cache entry disappeared before commit: " + sourcePath);
            }

            TextureCacheArtifact artifact = entry.Value.WithLastUsed(
                DateTime.UtcNow.Ticks);
            writer.Upsert(key, artifact, entry.Stamp);
            dirty = true;
        }


        internal void RegisterExisting(
            string packageId,
            string sourcePath,
            FileInfo source,
            string sourceHash,
            string converterIdentity,
            string cachePath,
            bool createdAfterOpen)
        {
            string relativeCachePath = GetRelativeCachePath(cachePath);
            FileInfo cacheFile = new FileInfo(cachePath);
            string key = TextureCacheIdentity.GetEntryKey(packageId, sourcePath);
            bool hadPrevious = writer.TryGet(
                key,
                out CacheEntry<TextureCacheArtifact, TextureCacheFingerprint> previous);
            bool sameFile = hadPrevious &&
                            string.Equals(
                                previous.Value.CachePath,
                                relativeCachePath,
                                StringComparison.OrdinalIgnoreCase);
            if (!sameFile && hadPrevious)
            {
                RemoveEntry(key);
            }

            if (!sameFile && (createdAfterOpen || !recoveryRequired))
            {
                currentBytes += cacheFile.Length;
            }

            writer.Upsert(
                key,
                new TextureCacheArtifact(
                    TextureCacheIdentity.Normalize(packageId),
                    TextureCacheIdentity.Normalize(sourcePath),
                    relativeCachePath,
                    cacheFile.Length,
                    DateTime.UtcNow.Ticks),
                new TextureCacheFingerprint(
                    source.Length,
                    source.LastWriteTimeUtc.Ticks,
                    sourceHash,
                    converterIdentity));
            dirty = true;
        }

        internal int RemoveMissingSources(
            string packageId,
            ISet<string> retainedSourcePaths)
        {
            string normalizedPackageId = TextureCacheIdentity.Normalize(packageId);
            string[] keys = writer.SnapshotEntries()
                .Where(pair =>
                    string.Equals(
                        pair.Value.Value.PackageId,
                        normalizedPackageId,
                        StringComparison.Ordinal) &&
                    !retainedSourcePaths.Contains(pair.Value.Value.SourcePath))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (string key in keys)
            {
                RemoveEntry(key);
            }

            return keys.Length;
        }

        internal int RemoveInactivePackages(ISet<string> activePackageIds)
        {
            string[] keys = writer.SnapshotEntries()
                .Where(pair =>
                    !activePackageIds.Contains(pair.Value.Value.PackageId))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (string key in keys)
            {
                RemoveEntry(key);
            }

            return keys.Length;
        }

        internal int SweepOrphans()
        {
            HashSet<string> retainedFiles = new HashSet<string>(
                writer.SnapshotEntries()
                    .Select(pair => pair.Value.Value.CachePath)
                    .Select(path => TryResolveCachePath(cacheRoot, path, out string resolved)
                        ? resolved
                        : null)
                    .Where(path => path != null),
                StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            foreach (string file in Directory.EnumerateFiles(
                         cacheRoot,
                         "*.dds",
                         SearchOption.AllDirectories))
            {
                string resolved = Path.GetFullPath(file);
                if (retainedFiles.Contains(resolved))
                {
                    continue;
                }

                File.Delete(resolved);
                removed++;
            }

            foreach (string directory in Directory.EnumerateDirectories(
                         cacheRoot,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length)
                     .ToArray())
            {
                if (Path.GetFileName(directory)
                        .StartsWith(".staging-", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(directory, true);
                    continue;
                }

                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            return removed;
        }

        internal int EnforceBudget(long maximumBytes)
        {
            if (maximumBytes <= 0L || currentBytes <= maximumBytes)
            {
                return 0;
            }

            int removed = 0;
            foreach (KeyValuePair<
                         string,
                         CacheEntry<TextureCacheArtifact, TextureCacheFingerprint>> pair in
                     writer.SnapshotEntries()
                         .OrderBy(item => item.Value.Value.LastUsedUtcTicks)
                         .ToArray())
            {
                RemoveEntry(pair.Key);
                removed++;
                if (currentBytes <= maximumBytes)
                {
                    break;
                }
            }

            return removed;
        }

        internal void Save()
        {
            writer.Publish();
            if (recoveryRequired)
            {
                currentBytes = GetDdsSize(cacheRoot);
                recoveryRequired = false;
                dirty = true;
            }

            if (!dirty && File.Exists(indexPath))
            {
                return;
            }

            TextureCacheManifest document = new TextureCacheManifest
            {
                SchemaVersion = DdsCacheContract.ManifestSchemaVersion,
                CacheIdentity = cacheIdentity,
                WrittenUtcTicks = DateTime.UtcNow.Ticks,
                TotalBytes = currentBytes,
                Entries = writer.SnapshotEntries()
                    .Select(ToDocumentEntry)
                    .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.SourcePath, StringComparer.Ordinal)
                    .ToList()
            };
            WriteAtomic(document);
            dirty = false;
        }

        public void Dispose()
        {
            writerLock.Dispose();
        }

        private static Dictionary<
            string,
            CacheEntry<TextureCacheArtifact, TextureCacheFingerprint>> LoadEntries(
            string cacheRoot,
            IEnumerable<TextureCacheManifestEntry> serializedEntries)
        {
            Dictionary<
                string,
                CacheEntry<TextureCacheArtifact, TextureCacheFingerprint>> result =
                new Dictionary<
                    string,
                    CacheEntry<TextureCacheArtifact, TextureCacheFingerprint>>(
                    StringComparer.Ordinal);
            if (serializedEntries == null)
            {
                return result;
            }

            foreach (TextureCacheManifestEntry entry in serializedEntries)
            {
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.PackageId) ||
                    string.IsNullOrWhiteSpace(entry.SourcePath) ||
                    entry.CacheBytes < 0L ||
                    !TryResolveCachePath(cacheRoot, entry.CachePath, out _))
                {
                    continue;
                }

                string packageId = TextureCacheIdentity.Normalize(entry.PackageId);
                string sourcePath = TextureCacheIdentity.Normalize(entry.SourcePath);
                result[TextureCacheIdentity.GetEntryKey(packageId, sourcePath)] =
                    new CacheEntry<TextureCacheArtifact, TextureCacheFingerprint>(
                        new TextureCacheArtifact(
                            packageId,
                            sourcePath,
                            entry.CachePath,
                            entry.CacheBytes,
                            entry.LastUsedUtcTicks),
                        new TextureCacheFingerprint(
                            entry.SourceLength,
                            entry.SourceWriteTimeUtcTicks,
                            entry.SourceHash,
                            entry.ConverterIdentity));
            }

            return result;
        }

        private void RemoveEntry(string key)
        {
            if (!writer.TryGet(
                    key,
                    out CacheEntry<TextureCacheArtifact, TextureCacheFingerprint> entry))
            {
                return;
            }

            TextureCacheArtifact artifact = entry.Value;
            writer.Remove(key);
            currentBytes = Math.Max(
                0L,
                currentBytes - Math.Max(0L, artifact.CacheBytes));
            dirty = true;
        }

        private void WriteAtomic(TextureCacheManifest document)
        {
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(TextureCacheManifest));
            AtomicFile.Write(
                indexPath,
                stream => serializer.WriteObject(stream, document),
                backupPath);
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
                    using (MemoryStream stream = new MemoryStream(bytes, writable: false))
                    {
                        return serializer.ReadObject(stream) as TextureCacheManifest;
                    }
                }

                using (FileStream stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read))
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
            TextureCacheManifest document,
            string cacheIdentity)
        {
            return document != null &&
                   document.SchemaVersion == DdsCacheContract.ManifestSchemaVersion &&
                   string.Equals(
                       document.CacheIdentity,
                       cacheIdentity,
                       StringComparison.Ordinal);
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

        private static TextureCacheManifestEntry ToDocumentEntry(
            KeyValuePair<
                string,
                CacheEntry<TextureCacheArtifact, TextureCacheFingerprint>> pair)
        {
            TextureCacheArtifact artifact = pair.Value.Value;
            TextureCacheFingerprint fingerprint = pair.Value.Stamp;
            return new TextureCacheManifestEntry
            {
                PackageId = artifact.PackageId,
                SourcePath = artifact.SourcePath,
                SourceLength = fingerprint.SourceLength,
                SourceWriteTimeUtcTicks = fingerprint.SourceWriteTimeUtcTicks,
                SourceHash = fingerprint.SourceHash,
                ConverterIdentity = fingerprint.ConverterIdentity,
                CachePath = artifact.CachePath,
                CacheBytes = artifact.CacheBytes,
                LastUsedUtcTicks = artifact.LastUsedUtcTicks
            };
        }

        private string GetRelativeCachePath(string path)
        {
            string resolvedRoot = cacheRoot.TrimEnd(
                                      Path.DirectorySeparatorChar,
                                      Path.AltDirectorySeparatorChar) +
                                  Path.DirectorySeparatorChar;
            string resolvedPath = Path.GetFullPath(path);
            if (!resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Path.GetExtension(resolvedPath),
                    ".dds",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid DDS cache path: " + resolvedPath);
            }

            return resolvedPath.Substring(resolvedRoot.Length)
                .Replace('\\', '/');
        }

        internal static bool TryResolveCachePath(
            string cacheRoot,
            string relativePath,
            out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath) ||
                !string.Equals(
                    Path.GetExtension(relativePath),
                    ".dds",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string rootPrefix = Path.GetFullPath(cacheRoot).TrimEnd(
                                    Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(
                cacheRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            resolvedPath = candidate;
            return true;
        }

        private static long GetDdsSize(string cacheRoot)
        {
            long bytes = 0L;
            foreach (string file in Directory.EnumerateFiles(
                         cacheRoot,
                         "*.dds",
                         SearchOption.AllDirectories))
            {
                bytes += new FileInfo(file).Length;
            }

            return bytes;
        }

    }

    internal readonly struct TextureCacheFingerprint
    {
        internal readonly long SourceLength;
        internal readonly long SourceWriteTimeUtcTicks;
        internal readonly string SourceHash;
        internal readonly string ConverterIdentity;

        internal TextureCacheFingerprint(
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
    }

    internal readonly struct TextureCacheArtifact
    {
        internal readonly string PackageId;
        internal readonly string SourcePath;
        internal readonly string CachePath;
        internal readonly long CacheBytes;
        internal readonly long LastUsedUtcTicks;

        internal TextureCacheArtifact(
            string packageId,
            string sourcePath,
            string cachePath,
            long cacheBytes,
            long lastUsedUtcTicks)
        {
            PackageId = packageId;
            SourcePath = sourcePath;
            CachePath = cachePath;
            CacheBytes = cacheBytes;
            LastUsedUtcTicks = lastUsedUtcTicks;
        }

        internal TextureCacheArtifact WithLastUsed(long lastUsedUtcTicks)
        {
            return new TextureCacheArtifact(
                PackageId,
                SourcePath,
                CachePath,
                CacheBytes,
                lastUsedUtcTicks);
        }
    }

    internal sealed class TextureCacheSnapshot
    {
        private readonly string cacheRoot;
        private readonly CacheSnapshot<
            string,
            TextureCacheArtifact,
            TextureCacheFingerprint> snapshot;

        internal TextureCacheSnapshot(
            string cacheRoot,
            CacheSnapshot<
                string,
                TextureCacheArtifact,
                TextureCacheFingerprint> snapshot)
        {
            this.cacheRoot = cacheRoot;
            this.snapshot = snapshot;
        }

        internal bool TryGetFresh(
            string packageId,
            string sourcePath,
            FileInfo source,
            string converterIdentity,
            out string cachePath)
        {
            cachePath = null;
            if (!snapshot.TryGet(
                    TextureCacheIdentity.GetEntryKey(packageId, sourcePath),
                    out CacheEntry<TextureCacheArtifact, TextureCacheFingerprint> entry) ||
                entry.Stamp.SourceLength != source.Length ||
                entry.Stamp.SourceWriteTimeUtcTicks !=
                source.LastWriteTimeUtc.Ticks ||
                !TextureCacheStore.ConverterMatches(
                    entry.Stamp.ConverterIdentity,
                    converterIdentity) ||
                !TryResolveArtifact(entry.Value, out cachePath))
            {
                cachePath = null;
                return false;
            }
            return true;
        }

        internal bool TryGetReusable(
            string packageId,
            string sourcePath,
            string sourceHash,
            string converterIdentity,
            out string cachePath)
        {
            cachePath = null;
            if (!snapshot.TryGet(
                    TextureCacheIdentity.GetEntryKey(packageId, sourcePath),
                    out CacheEntry<TextureCacheArtifact, TextureCacheFingerprint> entry) ||
                string.IsNullOrEmpty(entry.Stamp.SourceHash) ||
                !string.Equals(
                    entry.Stamp.SourceHash,
                    sourceHash,
                    StringComparison.Ordinal) ||
                !TextureCacheStore.ConverterMatches(
                    entry.Stamp.ConverterIdentity,
                    converterIdentity) ||
                !TryResolveArtifact(entry.Value, out cachePath))
            {
                cachePath = null;
                return false;
            }
            return true;
        }

        private bool TryResolveArtifact(
            TextureCacheArtifact artifact,
            out string cachePath)
        {
            return TextureCacheStore.TryResolveCachePath(
                       cacheRoot,
                       artifact.CachePath,
                       out cachePath) &&
                   File.Exists(cachePath) &&
                   new FileInfo(cachePath).Length == artifact.CacheBytes;
        }

    }

}
