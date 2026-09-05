using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FixWorld.Textures
{
    internal static class DdsCacheContract
    {
        internal const int ManifestSchemaVersion = 2;
        internal const string CacheIdentityVersion = "bc7-unorm-gpu-mips-v1-mod-pack";
        internal const string CacheDirectoryName = "dds-pack-v1";
        internal const string PackFileExtension = ".fwdp";
        internal const string IndexFileName = "index.json";
        internal const string BackupFileName = "index.backup.json";
        internal const string LockFileName = "index.lock";
        internal const string EnabledEnvironmentVariable = "FIXWORLD_DDS_CACHE";
        internal const string CacheRootEnvironmentVariable = "FIXWORLD_DDS_CACHE_ROOT";
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

        [DataMember(Name = "cacheOffset", Order = 9)]
        public long CacheOffset { get; set; }

        [DataMember(Name = "lastUsedUtcTicks", Order = 10)]
        public long LastUsedUtcTicks { get; set; }
    }
}
