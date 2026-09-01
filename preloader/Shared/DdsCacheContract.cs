using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;

namespace FixWorld.Textures
{
    internal readonly struct DdsReadAheadSnapshot
    {
        internal string Status { get; }
        internal long BudgetBytes { get; }
        internal long BytesRead { get; }
        internal int FilesRead { get; }
        internal double ElapsedMilliseconds { get; }
        internal bool IndexPrefetched { get; }
        internal string Error { get; }

        internal DdsReadAheadSnapshot(
            string status,
            long budgetBytes,
            long bytesRead,
            int filesRead,
            double elapsedMilliseconds,
            bool indexPrefetched,
            string error)
        {
            Status = status;
            BudgetBytes = budgetBytes;
            BytesRead = bytesRead;
            FilesRead = filesRead;
            ElapsedMilliseconds = elapsedMilliseconds;
            IndexPrefetched = indexPrefetched;
            Error = error;
        }
    }

    internal static class DdsCacheContract
    {
        internal const int ManifestSchemaVersion = 1;
        internal const string CacheIdentityVersion =
            "bc3-unorm-mips-v4-ignore-srgb-content-index";
        internal const string CacheDirectoryName = "dds-v1";
        internal const string IndexFileName = "index.json";
        internal const string BackupFileName = "index.backup.json";
        internal const string LockFileName = "index.lock";
        internal const string EnabledEnvironmentVariable = "FIXWORLD_DDS_CACHE";
        internal const string CacheRootEnvironmentVariable =
            "FIXWORLD_DDS_CACHE_ROOT";
        internal const string ReadAheadMiBEnvironmentVariable =
            "FIXWORLD_DDS_READ_AHEAD_MIB";

        private const string IndexBytesKey = "FixWorld.DdsCache.IndexBytes";
        private const string IndexPathKey = "FixWorld.DdsCache.IndexPath";
        private const string IndexLengthKey = "FixWorld.DdsCache.IndexLength";
        private const string IndexWriteTicksKey = "FixWorld.DdsCache.IndexWriteTicks";
        private const string StopKey = "FixWorld.DdsCache.ReadAheadStop";
        private const string StatusVariable = "FIXWORLD_DDS_READ_AHEAD_STATUS";
        private const string BudgetBytesVariable =
            "FIXWORLD_DDS_READ_AHEAD_BUDGET_BYTES";
        private const string BytesReadVariable =
            "FIXWORLD_DDS_READ_AHEAD_BYTES";
        private const string FilesReadVariable =
            "FIXWORLD_DDS_READ_AHEAD_FILES";
        private const string ElapsedMillisecondsVariable =
            "FIXWORLD_DDS_READ_AHEAD_MS";
        private const string IndexPrefetchedVariable =
            "FIXWORLD_DDS_INDEX_PREFETCHED";
        private const string ErrorVariable = "FIXWORLD_DDS_READ_AHEAD_ERROR";

        internal static void PublishIndex(
            string path,
            long length,
            long lastWriteUtcTicks,
            byte[] bytes)
        {
            AppDomain.CurrentDomain.SetData(IndexPathKey, Path.GetFullPath(path));
            AppDomain.CurrentDomain.SetData(IndexLengthKey, length);
            AppDomain.CurrentDomain.SetData(IndexWriteTicksKey, lastWriteUtcTicks);
            AppDomain.CurrentDomain.SetData(IndexBytesKey, bytes);
            Set(IndexPrefetchedVariable, "1");
        }

        internal static bool TryGetPublishedIndex(string path, out byte[] bytes)
        {
            bytes = null;
            try
            {
                string publishedPath = AppDomain.CurrentDomain.GetData(IndexPathKey) as string;
                byte[] publishedBytes = AppDomain.CurrentDomain.GetData(IndexBytesKey) as byte[];
                if (publishedBytes == null ||
                    !string.Equals(
                        publishedPath,
                        Path.GetFullPath(path),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                FileInfo file = new FileInfo(path);
                if (!file.Exists ||
                    !(AppDomain.CurrentDomain.GetData(IndexLengthKey) is long length) ||
                    !(AppDomain.CurrentDomain.GetData(IndexWriteTicksKey) is long writeTicks) ||
                    file.Length != length ||
                    file.LastWriteTimeUtc.Ticks != writeTicks)
                {
                    return false;
                }

                bytes = publishedBytes;
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static void RequestReadAheadStop()
        {
            AppDomain.CurrentDomain.SetData(StopKey, true);
        }

        internal static bool IsReadAheadStopRequested()
        {
            return AppDomain.CurrentDomain.GetData(StopKey) is bool stopped && stopped;
        }

        internal static void PublishReadAhead(
            string status,
            long budgetBytes,
            long bytesRead,
            int filesRead,
            double elapsedMilliseconds,
            string error = null)
        {
            Set(StatusVariable, status);
            Set(BudgetBytesVariable, budgetBytes);
            Set(BytesReadVariable, bytesRead);
            Set(FilesReadVariable, filesRead);
            Set(
                ElapsedMillisecondsVariable,
                elapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
            Set(ErrorVariable, error);
        }

        internal static DdsReadAheadSnapshot CaptureReadAhead()
        {
            return new DdsReadAheadSnapshot(
                Get(StatusVariable) ?? "inactive",
                GetLong(BudgetBytesVariable),
                GetLong(BytesReadVariable),
                GetInt(FilesReadVariable),
                GetDouble(ElapsedMillisecondsVariable),
                string.Equals(Get(IndexPrefetchedVariable), "1", StringComparison.Ordinal),
                Get(ErrorVariable));
        }

        private static int GetInt(string name)
        {
            return int.TryParse(
                Get(name),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : 0;
        }

        private static long GetLong(string name)
        {
            return long.TryParse(
                Get(name),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long value)
                ? value
                : 0L;
        }

        private static double GetDouble(string name)
        {
            return double.TryParse(
                Get(name),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value)
                ? value
                : 0.0;
        }

        private static string Get(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        private static void Set(string name, int value)
        {
            Set(name, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Set(string name, long value)
        {
            Set(name, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Set(string name, string value)
        {
            Environment.SetEnvironmentVariable(
                name,
                value,
                EnvironmentVariableTarget.Process);
        }
    }

    [DataContract]
    internal sealed class TextureCacheManifest
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
        public List<TextureCacheManifestEntry> Entries { get; set; }
    }

    [DataContract]
    internal sealed class TextureCacheManifestEntry
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
}
