using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FixWorld.Caching
{
    internal sealed class TextureCacheIndex : IDisposable
    {
        private const int CurrentSchemaVersion = 1;
        private const string IndexFileName = "index.json";
        private const string BackupFileName = "index.backup.json";
        private const string LockFileName = "index.lock";

        private readonly string cacheRoot;
        private readonly string indexPath;
        private readonly string backupPath;
        private readonly string cacheIdentity;
        private readonly Dictionary<string, TextureCacheIndexEntry> entries;
        private readonly FileStream writerLock;

        private bool dirty;
        private bool recoveryRequired;
        private long currentBytes;

        private TextureCacheIndex(
            string cacheRoot,
            string cacheIdentity,
            Dictionary<string, TextureCacheIndexEntry> entries,
            long currentBytes,
            bool recoveryRequired,
            FileStream writerLock,
            string loadStatus)
        {
            this.cacheRoot = cacheRoot;
            this.cacheIdentity = cacheIdentity;
            this.entries = entries;
            this.currentBytes = currentBytes;
            this.recoveryRequired = recoveryRequired;
            this.writerLock = writerLock;
            indexPath = Path.Combine(cacheRoot, IndexFileName);
            backupPath = Path.Combine(cacheRoot, BackupFileName);
            LoadStatus = loadStatus;
        }

        internal long CurrentBytes => currentBytes;

        internal int EntryCount => entries.Count;

        internal bool RecoveryRequired => recoveryRequired;

        internal string LoadStatus { get; }

        internal static TextureCacheIndex Open(string cacheRoot, string cacheIdentity)
        {
            string resolvedRoot = Path.GetFullPath(cacheRoot);
            Directory.CreateDirectory(resolvedRoot);
            string lockPath = Path.Combine(resolvedRoot, LockFileName);
            FileStream writerLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            try
            {
                string indexPath = Path.Combine(resolvedRoot, IndexFileName);
                string backupPath = Path.Combine(resolvedRoot, BackupFileName);
                TextureCacheIndexDocument document = TryRead(indexPath) ?? TryRead(backupPath);
                if (document != null &&
                    document.SchemaVersion == CurrentSchemaVersion &&
                    string.Equals(
                        document.CacheIdentity,
                        cacheIdentity,
                        StringComparison.Ordinal))
                {
                    Dictionary<string, TextureCacheIndexEntry> loadedEntries =
                        LoadEntries(resolvedRoot, document.Entries);
                    long bytes = loadedEntries.Values.Sum(entry => Math.Max(0L, entry.CacheBytes));
                    return new TextureCacheIndex(
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
                return new TextureCacheIndex(
                    resolvedRoot,
                    cacheIdentity,
                    new Dictionary<string, TextureCacheIndexEntry>(StringComparer.Ordinal),
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

        internal bool TryGetFresh(
            string packageId,
            string sourcePath,
            FileInfo source,
            string converterIdentity,
            out string cachePath)
        {
            cachePath = null;
            string key = GetKey(packageId, sourcePath);
            if (!entries.TryGetValue(key, out TextureCacheIndexEntry entry) ||
                entry.SourceLength != source.Length ||
                entry.SourceWriteTimeUtcTicks != source.LastWriteTimeUtc.Ticks ||
                !ConverterMatches(entry, converterIdentity))
            {
                return false;
            }

            if (!TryResolveValidCacheFile(entry, out cachePath))
            {
                RemoveEntry(key, deleteFile: true);
                cachePath = null;
                return false;
            }

            entry.LastUsedUtcTicks = DateTime.UtcNow.Ticks;
            dirty = true;
            return true;
        }

        internal TextureCacheValidationIndex CreateValidationSnapshot()
        {
            Dictionary<string, TextureCacheValidationIndexEntry> snapshotEntries =
                new Dictionary<string, TextureCacheValidationIndexEntry>(
                    entries.Count,
                    StringComparer.Ordinal);
            foreach (KeyValuePair<string, TextureCacheIndexEntry> pair in entries)
            {
                TextureCacheIndexEntry entry = pair.Value;
                if (!TryResolveCachePath(cacheRoot, entry.CachePath, out string cachePath))
                {
                    continue;
                }

                snapshotEntries.Add(
                    pair.Key,
                    new TextureCacheValidationIndexEntry(
                        entry.SourceLength,
                        entry.SourceWriteTimeUtcTicks,
                        entry.SourceHash,
                        entry.ConverterIdentity,
                        cachePath,
                        entry.CacheBytes));
            }

            return new TextureCacheValidationIndex(snapshotEntries);
        }

        internal bool MatchesPrepared(
            string packageId,
            string sourcePath,
            long sourceLength,
            long sourceWriteTimeUtcTicks,
            string converterIdentity,
            string cachePath)
        {
            if (!entries.TryGetValue(
                    GetKey(packageId, sourcePath),
                    out TextureCacheIndexEntry entry) ||
                entry.SourceLength != sourceLength ||
                entry.SourceWriteTimeUtcTicks != sourceWriteTimeUtcTicks ||
                !ConverterMatches(entry, converterIdentity) ||
                !TryResolveCachePath(cacheRoot, entry.CachePath, out string resolvedPath))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(cachePath),
                resolvedPath,
                StringComparison.OrdinalIgnoreCase);
        }

        internal void TouchPrepared(string packageId, string sourcePath)
        {
            if (!entries.TryGetValue(
                    GetKey(packageId, sourcePath),
                    out TextureCacheIndexEntry entry))
            {
                throw new InvalidOperationException(
                    "Prepared DDS cache entry disappeared before commit: " + sourcePath);
            }

            entry.LastUsedUtcTicks = DateTime.UtcNow.Ticks;
            dirty = true;
        }

        internal bool TryReuseContent(
            string packageId,
            string sourcePath,
            FileInfo source,
            string sourceHash,
            string converterIdentity,
            out string cachePath)
        {
            cachePath = null;
            string key = GetKey(packageId, sourcePath);
            if (!entries.TryGetValue(key, out TextureCacheIndexEntry entry) ||
                string.IsNullOrEmpty(entry.SourceHash) ||
                !string.Equals(entry.SourceHash, sourceHash, StringComparison.Ordinal) ||
                !ConverterMatches(entry, converterIdentity) ||
                !TryResolveValidCacheFile(entry, out cachePath))
            {
                return false;
            }

            entry.SourceLength = source.Length;
            entry.SourceWriteTimeUtcTicks = source.LastWriteTimeUtc.Ticks;
            entry.LastUsedUtcTicks = DateTime.UtcNow.Ticks;
            dirty = true;
            return true;
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
            string key = GetKey(packageId, sourcePath);
            bool sameFile = entries.TryGetValue(key, out TextureCacheIndexEntry previous) &&
                            string.Equals(
                                previous.CachePath,
                                relativeCachePath,
                                StringComparison.OrdinalIgnoreCase);
            if (!sameFile && previous != null)
            {
                RemoveEntry(key, deleteFile: true);
            }

            if (!sameFile && (createdAfterOpen || !recoveryRequired))
            {
                currentBytes += cacheFile.Length;
            }

            entries[key] = new TextureCacheIndexEntry
            {
                PackageId = Normalize(packageId),
                SourcePath = Normalize(sourcePath),
                SourceLength = source.Length,
                SourceWriteTimeUtcTicks = source.LastWriteTimeUtc.Ticks,
                SourceHash = sourceHash,
                ConverterIdentity = converterIdentity,
                CachePath = relativeCachePath,
                CacheBytes = cacheFile.Length,
                LastUsedUtcTicks = DateTime.UtcNow.Ticks
            };
            dirty = true;
        }

        internal void RemoveSource(string packageId, string sourcePath)
        {
            RemoveEntry(GetKey(packageId, sourcePath), deleteFile: true);
        }

        internal int RemoveMissingSources(
            string packageId,
            ISet<string> retainedSourcePaths)
        {
            string normalizedPackageId = Normalize(packageId);
            string[] keys = entries
                .Where(pair =>
                    string.Equals(
                        pair.Value.PackageId,
                        normalizedPackageId,
                        StringComparison.Ordinal) &&
                    !retainedSourcePaths.Contains(pair.Value.SourcePath))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (string key in keys)
            {
                RemoveEntry(key, deleteFile: true);
            }

            return keys.Length;
        }

        internal int RemoveInactivePackages(ISet<string> activePackageIds)
        {
            string[] keys = entries
                .Where(pair => !activePackageIds.Contains(pair.Value.PackageId))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (string key in keys)
            {
                RemoveEntry(key, deleteFile: true);
            }

            return keys.Length;
        }

        internal int EnforceBudget(long maximumBytes)
        {
            if (maximumBytes <= 0L || currentBytes <= maximumBytes)
            {
                return 0;
            }

            int removed = 0;
            foreach (KeyValuePair<string, TextureCacheIndexEntry> pair in entries
                         .OrderBy(item => item.Value.LastUsedUtcTicks)
                         .ToArray())
            {
                RemoveEntry(pair.Key, deleteFile: true);
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

            TextureCacheIndexDocument document = new TextureCacheIndexDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                CacheIdentity = cacheIdentity,
                WrittenUtcTicks = DateTime.UtcNow.Ticks,
                TotalBytes = currentBytes,
                Entries = entries.Values
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

        private static Dictionary<string, TextureCacheIndexEntry> LoadEntries(
            string cacheRoot,
            IEnumerable<TextureCacheIndexEntry> serializedEntries)
        {
            Dictionary<string, TextureCacheIndexEntry> result =
                new Dictionary<string, TextureCacheIndexEntry>(StringComparer.Ordinal);
            if (serializedEntries == null)
            {
                return result;
            }

            foreach (TextureCacheIndexEntry entry in serializedEntries)
            {
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.PackageId) ||
                    string.IsNullOrWhiteSpace(entry.SourcePath) ||
                    entry.CacheBytes < 0L ||
                    !TryResolveCachePath(cacheRoot, entry.CachePath, out _))
                {
                    continue;
                }

                entry.PackageId = Normalize(entry.PackageId);
                entry.SourcePath = Normalize(entry.SourcePath);
                result[GetKey(entry.PackageId, entry.SourcePath)] = entry;
            }

            return result;
        }

        private bool TryResolveValidCacheFile(
            TextureCacheIndexEntry entry,
            out string path)
        {
            if (!TryResolveCachePath(cacheRoot, entry.CachePath, out path) ||
                !File.Exists(path))
            {
                return false;
            }

            return new FileInfo(path).Length == entry.CacheBytes;
        }

        private void RemoveEntry(string key, bool deleteFile)
        {
            if (!entries.TryGetValue(key, out TextureCacheIndexEntry entry))
            {
                return;
            }

            entries.Remove(key);
            currentBytes = Math.Max(0L, currentBytes - Math.Max(0L, entry.CacheBytes));
            dirty = true;
            if (!deleteFile || !TryResolveCachePath(cacheRoot, entry.CachePath, out string path))
            {
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) &&
                !string.Equals(parent, cacheRoot, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(parent) &&
                !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }

        private void WriteAtomic(TextureCacheIndexDocument document)
        {
            string temporaryPath = indexPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(TextureCacheIndexDocument));
                using (FileStream stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    serializer.WriteObject(stream, document);
                    stream.Flush(true);
                }

                if (File.Exists(indexPath))
                {
                    File.Replace(temporaryPath, indexPath, backupPath);
                }
                else
                {
                    File.Move(temporaryPath, indexPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static TextureCacheIndexDocument TryRead(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(TextureCacheIndexDocument));
                using (FileStream stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read))
                {
                    return serializer.ReadObject(stream) as TextureCacheIndexDocument;
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

        private static bool ConverterMatches(
            TextureCacheIndexEntry entry,
            string converterIdentity)
        {
            return string.IsNullOrEmpty(converterIdentity) ||
                   string.Equals(
                       entry.ConverterIdentity,
                       converterIdentity,
                       StringComparison.Ordinal);
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

        private static bool TryResolveCachePath(
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

        private static string GetKey(string packageId, string sourcePath)
        {
            return Normalize(packageId) + "\n" + Normalize(sourcePath);
        }

        private static string Normalize(string value)
        {
            return value.Replace('\\', '/').ToLowerInvariant();
        }
    }

    [DataContract]
    internal sealed class TextureCacheIndexDocument
    {
        [DataMember(Name = "schemaVersion", Order = 1)]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "cacheIdentity", Order = 2)]
        public string CacheIdentity { get; set; }

        [DataMember(Name = "writtenUtcTicks", Order = 3)]
        public long WrittenUtcTicks { get; set; }

        [DataMember(Name = "totalBytes", Order = 4)]
        public long TotalBytes { get; set; }

        [DataMember(Name = "entries", Order = 5)]
        public List<TextureCacheIndexEntry> Entries { get; set; }
    }

    [DataContract]
    internal sealed class TextureCacheIndexEntry
    {
        [DataMember(Name = "packageId", Order = 1)]
        public string PackageId { get; set; }

        [DataMember(Name = "sourcePath", Order = 2)]
        public string SourcePath { get; set; }

        [DataMember(Name = "sourceLength", Order = 3)]
        public long SourceLength { get; set; }

        [DataMember(Name = "sourceWriteTimeUtcTicks", Order = 4)]
        public long SourceWriteTimeUtcTicks { get; set; }

        [DataMember(Name = "sourceHash", Order = 5, EmitDefaultValue = false)]
        public string SourceHash { get; set; }

        [DataMember(Name = "converterIdentity", Order = 6, EmitDefaultValue = false)]
        public string ConverterIdentity { get; set; }

        [DataMember(Name = "cachePath", Order = 7)]
        public string CachePath { get; set; }

        [DataMember(Name = "cacheBytes", Order = 8)]
        public long CacheBytes { get; set; }

        [DataMember(Name = "lastUsedUtcTicks", Order = 9)]
        public long LastUsedUtcTicks { get; set; }
    }

    internal sealed class TextureCacheValidationIndex
    {
        private readonly IReadOnlyDictionary<string, TextureCacheValidationIndexEntry>
            entries;

        internal TextureCacheValidationIndex(
            IReadOnlyDictionary<string, TextureCacheValidationIndexEntry> entries)
        {
            this.entries = entries;
        }

        internal bool TryGetFresh(
            string packageId,
            string sourcePath,
            FileInfo source,
            string converterIdentity,
            out string cachePath)
        {
            cachePath = null;
            if (!entries.TryGetValue(
                    GetKey(packageId, sourcePath),
                    out TextureCacheValidationIndexEntry entry) ||
                entry.SourceLength != source.Length ||
                entry.SourceWriteTimeUtcTicks != source.LastWriteTimeUtc.Ticks ||
                !ConverterMatches(entry.ConverterIdentity, converterIdentity) ||
                !File.Exists(entry.CachePath) ||
                new FileInfo(entry.CachePath).Length != entry.CacheBytes)
            {
                return false;
            }

            cachePath = entry.CachePath;
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
            if (!entries.TryGetValue(
                    GetKey(packageId, sourcePath),
                    out TextureCacheValidationIndexEntry entry) ||
                string.IsNullOrEmpty(entry.SourceHash) ||
                !string.Equals(entry.SourceHash, sourceHash, StringComparison.Ordinal) ||
                !ConverterMatches(entry.ConverterIdentity, converterIdentity) ||
                !File.Exists(entry.CachePath) ||
                new FileInfo(entry.CachePath).Length != entry.CacheBytes)
            {
                return false;
            }

            cachePath = entry.CachePath;
            return true;
        }

        private static bool ConverterMatches(string indexed, string current)
        {
            return string.IsNullOrEmpty(current) ||
                   string.Equals(indexed, current, StringComparison.Ordinal);
        }

        private static string GetKey(string packageId, string sourcePath)
        {
            return Normalize(packageId) + "\n" + Normalize(sourcePath);
        }

        private static string Normalize(string value)
        {
            return value.Replace('\\', '/').ToLowerInvariant();
        }
    }

    internal readonly struct TextureCacheValidationIndexEntry
    {
        internal readonly long SourceLength;
        internal readonly long SourceWriteTimeUtcTicks;
        internal readonly string SourceHash;
        internal readonly string ConverterIdentity;
        internal readonly string CachePath;
        internal readonly long CacheBytes;

        internal TextureCacheValidationIndexEntry(
            long sourceLength,
            long sourceWriteTimeUtcTicks,
            string sourceHash,
            string converterIdentity,
            string cachePath,
            long cacheBytes)
        {
            SourceLength = sourceLength;
            SourceWriteTimeUtcTicks = sourceWriteTimeUtcTicks;
            SourceHash = sourceHash;
            ConverterIdentity = converterIdentity;
            CachePath = cachePath;
            CacheBytes = cacheBytes;
        }
    }
}
