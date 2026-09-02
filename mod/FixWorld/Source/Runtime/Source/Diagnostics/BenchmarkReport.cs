using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using FixWorld.Runtime;
using FixWorld.Preloader;
using FixWorld.Textures;
using FixWorld.Loading;

namespace FixWorld.Diagnostics
{
    [DataContract]
    internal sealed class BenchmarkReport
    {
        private const int CurrentSchemaVersion = 10;

        [DataMember(Name = "schemaVersion", Order = 1)]
        public int SchemaVersion { get; private set; }

        [DataMember(Name = "completedUtc", Order = 2)]
        public string CompletedUtc { get; private set; }

        [DataMember(Name = "preloader", Order = 3)]
        public PreloaderReport Preloader { get; private set; }

        [DataMember(Name = "completion", Order = 4)]
        public CompletionReport Completion { get; private set; }

        [DataMember(Name = "loader", Order = 5)]
        public LoaderReport Loader { get; private set; }

        [DataMember(Name = "files", Order = 6)]
        public FileDiscoveryReport Files { get; private set; }

        [DataMember(Name = "texturePaths", Order = 7)]
        public TexturePathReport TexturePaths { get; private set; }

        [DataMember(Name = "textures", Order = 8)]
        public TextureReport Textures { get; private set; }

        [DataMember(Name = "ddsCache", Order = 9)]
        public DdsCacheReport DdsCache { get; private set; }

        private BenchmarkReport()
        {
        }

        internal static BenchmarkReport Create(
            string completionSource,
            PreloaderTimelineSnapshot preloader,
            LoadingMeasurement loading,
            FileDiscoverySnapshot files,
            TexturePathSnapshot texturePaths,
            TextureProbeSnapshot textures,
            TextureDdsCacheSnapshot ddsCache)
        {
            List<LoaderStepReport> steps = loading.Steps
                .Select(step => new LoaderStepReport(step))
                .ToList();
            List<LoaderStageReport> stages = Enum
                .GetValues(typeof(LoadingStage))
                .Cast<LoadingStage>()
                .Select(stage => new LoaderStageReport(
                    stage,
                    steps.Where(step => step.Number == (int)stage).ToList()))
                .ToList();

            return new BenchmarkReport
            {
                SchemaVersion = CurrentSchemaVersion,
                CompletedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Preloader = new PreloaderReport(preloader),
                Completion = new CompletionReport(completionSource),
                Loader = new LoaderReport(
                    loading.ObservedMilliseconds,
                    stages,
                    steps),
                Files = new FileDiscoveryReport(files),
                TexturePaths = new TexturePathReport(texturePaths),
                Textures = new TextureReport(textures),
                DdsCache = new DdsCacheReport(ddsCache)
            };
        }

        internal void Write(string path)
        {
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(BenchmarkReport));
            AtomicFile.Write(path, stream => serializer.WriteObject(stream, this));
        }
    }

    [DataContract]
    internal sealed class PreloaderReport
    {
        [DataMember(Name = "active", Order = 1)]
        public bool Active { get; private set; }

        [DataMember(Name = "doorstopVersion", Order = 2)]
        public string DoorstopVersion { get; private set; }

        [DataMember(Name = "assemblyCSharpObserved", Order = 3)]
        public bool AssemblyCSharpObserved { get; private set; }

        [DataMember(Name = "assemblyCSharpAvailableAtEntry", Order = 4)]
        public bool AssemblyCSharpAvailableAtEntry { get; private set; }

        [DataMember(Name = "assembliesAtEntry", Order = 5)]
        public int AssembliesAtEntry { get; private set; }

        [DataMember(Name = "assembliesAtBootstrap", Order = 6)]
        public int AssembliesAtBootstrap { get; private set; }

        [DataMember(Name = "modAssembliesAtEntry", Order = 7)]
        public int ModAssembliesAtEntry { get; private set; }

        [DataMember(Name = "modAssembliesLoaded", Order = 8)]
        public int ModAssembliesLoaded { get; private set; }

        [DataMember(Name = "firstModAssembly", Order = 9)]
        public string FirstModAssembly { get; private set; }

        [DataMember(Name = "lastModAssembly", Order = 10)]
        public string LastModAssembly { get; private set; }

        [DataMember(Name = "entryToAssemblyCSharpMs", Order = 11)]
        public double? EntryToAssemblyCSharpMilliseconds { get; private set; }

        [DataMember(Name = "entryToFirstModAssemblyMs", Order = 12)]
        public double? EntryToFirstModAssemblyMilliseconds { get; private set; }

        [DataMember(Name = "entryToLastModAssemblyMs", Order = 13)]
        public double? EntryToLastModAssemblyMilliseconds { get; private set; }

        [DataMember(Name = "entryToBootstrapMs", Order = 14)]
        public double? EntryToBootstrapMilliseconds { get; private set; }

        [DataMember(Name = "assemblyCSharpToFirstModAssemblyMs", Order = 15)]
        public double? AssemblyCSharpToFirstModAssemblyMilliseconds { get; private set; }

        [DataMember(Name = "modAssemblyLoadMs", Order = 16)]
        public double? ModAssemblyLoadMilliseconds { get; private set; }

        [DataMember(Name = "lastModAssemblyToBootstrapMs", Order = 17)]
        public double? LastModAssemblyToBootstrapMilliseconds { get; private set; }

        [DataMember(Name = "ddsReadAheadStatus", Order = 18)]
        public string DdsReadAheadStatus { get; private set; }

        [DataMember(Name = "ddsReadAheadBudgetBytes", Order = 19)]
        public long DdsReadAheadBudgetBytes { get; private set; }

        [DataMember(Name = "ddsReadAheadBytes", Order = 20)]
        public long DdsReadAheadBytes { get; private set; }

        [DataMember(Name = "ddsReadAheadFiles", Order = 21)]
        public int DdsReadAheadFiles { get; private set; }

        [DataMember(Name = "ddsReadAheadMs", Order = 22)]
        public double DdsReadAheadMilliseconds { get; private set; }

        [DataMember(Name = "ddsIndexPrefetched", Order = 23)]
        public bool DdsIndexPrefetched { get; private set; }

        [DataMember(Name = "ddsReadAheadError", Order = 24)]
        public string DdsReadAheadError { get; private set; }

        internal PreloaderReport(PreloaderTimelineSnapshot snapshot)
        {
            DdsReadAheadSnapshot readAhead = DdsCacheContract.CaptureReadAhead();
            Active = snapshot.Active;
            DoorstopVersion = snapshot.DoorstopVersion;
            AssemblyCSharpObserved = snapshot.AssemblyCSharpObserved;
            AssemblyCSharpAvailableAtEntry = snapshot.AssemblyCSharpAvailableAtEntry;
            AssembliesAtEntry = snapshot.AssembliesAtEntry;
            AssembliesAtBootstrap = snapshot.AssembliesAtBootstrap;
            ModAssembliesAtEntry = snapshot.ModAssembliesAtEntry;
            ModAssembliesLoaded = snapshot.ModAssembliesLoaded;
            FirstModAssembly = snapshot.FirstModAssembly;
            LastModAssembly = snapshot.LastModAssembly;
            EntryToAssemblyCSharpMilliseconds = snapshot.EntryToAssemblyCSharpMilliseconds;
            EntryToFirstModAssemblyMilliseconds = snapshot.EntryToFirstModAssemblyMilliseconds;
            EntryToLastModAssemblyMilliseconds = snapshot.EntryToLastModAssemblyMilliseconds;
            EntryToBootstrapMilliseconds = snapshot.EntryToBootstrapMilliseconds;
            AssemblyCSharpToFirstModAssemblyMilliseconds =
                snapshot.AssemblyCSharpToFirstModAssemblyMilliseconds;
            ModAssemblyLoadMilliseconds = snapshot.ModAssemblyLoadMilliseconds;
            LastModAssemblyToBootstrapMilliseconds =
                snapshot.LastModAssemblyToBootstrapMilliseconds;
            DdsReadAheadStatus = readAhead.Status;
            DdsReadAheadBudgetBytes = readAhead.BudgetBytes;
            DdsReadAheadBytes = readAhead.BytesRead;
            DdsReadAheadFiles = readAhead.FilesRead;
            DdsReadAheadMilliseconds = readAhead.ElapsedMilliseconds;
            DdsIndexPrefetched = readAhead.IndexPrefetched;
            DdsReadAheadError = readAhead.Error;
        }
    }

    [DataContract]
    internal sealed class CompletionReport
    {
        [DataMember(Name = "source", Order = 1)]
        public string Source { get; private set; }

        internal CompletionReport(string source)
        {
            Source = source;
        }
    }

    [DataContract]
    internal sealed class LoaderReport
    {
        [DataMember(Name = "observedMs", Order = 1)]
        public double ObservedMilliseconds { get; private set; }

        [DataMember(Name = "stages", Order = 2)]
        public List<LoaderStageReport> Stages { get; private set; }

        [DataMember(Name = "steps", Order = 3)]
        public List<LoaderStepReport> Steps { get; private set; }

        internal LoaderReport(
            double observedMilliseconds,
            List<LoaderStageReport> stages,
            List<LoaderStepReport> steps)
        {
            ObservedMilliseconds = observedMilliseconds;
            Stages = stages;
            Steps = steps;
        }
    }

    [DataContract]
    internal sealed class LoaderStageReport
    {
        [DataMember(Name = "number", Order = 1)]
        public int Number { get; private set; }

        [DataMember(Name = "name", Order = 2)]
        public string Name { get; private set; }

        [DataMember(Name = "observed", Order = 3)]
        public bool Observed { get; private set; }

        [DataMember(Name = "exclusiveMs", Order = 4)]
        public double ExclusiveMilliseconds { get; private set; }

        [DataMember(Name = "mainThreadMs", Order = 5)]
        public double MainThreadMilliseconds { get; private set; }

        [DataMember(Name = "workerThreadMs", Order = 6)]
        public double WorkerThreadMilliseconds { get; private set; }

        internal LoaderStageReport(LoadingStage stage, IReadOnlyCollection<LoaderStepReport> steps)
        {
            Number = (int)stage;
            Name = LoadingStageNames.GetName(stage);
            Observed = steps.Count > 0;
            ExclusiveMilliseconds = steps.Sum(step => step.ExclusiveMilliseconds);
            MainThreadMilliseconds = steps.Sum(step => step.MainThreadExclusiveMilliseconds);
            WorkerThreadMilliseconds = steps.Sum(step => step.WorkerThreadExclusiveMilliseconds);
        }
    }

    [DataContract]
    internal sealed class LoaderStepReport
    {
        [DataMember(Name = "id", Order = 1)]
        public string Id { get; private set; }

        [DataMember(Name = "number", Order = 2)]
        public int Number { get; private set; }

        [DataMember(Name = "stage", Order = 3)]
        public string Stage { get; private set; }

        [DataMember(Name = "name", Order = 4)]
        public string Name { get; private set; }

        [DataMember(Name = "calls", Order = 5)]
        public long Calls { get; private set; }

        [DataMember(Name = "totalMs", Order = 6)]
        public double TotalMilliseconds { get; private set; }

        [DataMember(Name = "exclusiveMs", Order = 7)]
        public double ExclusiveMilliseconds { get; private set; }

        [DataMember(Name = "mainThreadMs", Order = 8)]
        public double MainThreadMilliseconds { get; private set; }

        [DataMember(Name = "workerThreadMs", Order = 9)]
        public double WorkerThreadMilliseconds { get; private set; }

        internal double MainThreadExclusiveMilliseconds { get; private set; }
        internal double WorkerThreadExclusiveMilliseconds { get; private set; }

        internal LoaderStepReport(LoadingStepMeasurement step)
        {
            Id = step.Step.ToString();
            Number = (int)step.Stage;
            Stage = LoadingStageNames.GetName(step.Stage);
            Name = step.Name;
            Calls = step.Calls;
            TotalMilliseconds = step.TotalMilliseconds;
            ExclusiveMilliseconds = step.ExclusiveMilliseconds;
            MainThreadMilliseconds = step.MainThreadMilliseconds;
            WorkerThreadMilliseconds = step.WorkerThreadMilliseconds;
            MainThreadExclusiveMilliseconds = step.MainThreadExclusiveMilliseconds;
            WorkerThreadExclusiveMilliseconds = step.WorkerThreadExclusiveMilliseconds;
        }
    }

    [DataContract]
    internal sealed class FileDiscoveryReport
    {
        [DataMember(Name = "calls", Order = 1)]
        public long Calls { get; private set; }

        [DataMember(Name = "files", Order = 2)]
        public long Files { get; private set; }

        [DataMember(Name = "totalMs", Order = 3)]
        public double TotalMilliseconds { get; private set; }

        [DataMember(Name = "textureCalls", Order = 4)]
        public long TextureCalls { get; private set; }

        [DataMember(Name = "textureFiles", Order = 5)]
        public long TextureFiles { get; private set; }

        [DataMember(Name = "textureMs", Order = 6)]
        public double TextureMilliseconds { get; private set; }

        internal FileDiscoveryReport(FileDiscoverySnapshot files)
        {
            Calls = files.Calls;
            Files = files.Files;
            TotalMilliseconds = files.TotalMilliseconds;
            TextureCalls = files.TextureCalls;
            TextureFiles = files.TextureFiles;
            TextureMilliseconds = files.TextureMilliseconds;
        }
    }

    [DataContract]
    internal sealed class TexturePathReport
    {
        [DataMember(Name = "unique", Order = 1)]
        public int Unique { get; private set; }

        [DataMember(Name = "duplicatePaths", Order = 2)]
        public int DuplicatePaths { get; private set; }

        [DataMember(Name = "potentiallyShadowedFiles", Order = 3)]
        public int PotentiallyShadowedFiles { get; private set; }

        [DataMember(Name = "potentiallyShadowedBytes", Order = 4)]
        public long PotentiallyShadowedBytes { get; private set; }

        [DataMember(Name = "topShadowedMods", Order = 5)]
        public List<ShadowedModReport> TopShadowedMods { get; private set; }

        internal TexturePathReport(TexturePathSnapshot paths)
        {
            Unique = paths.Unique;
            DuplicatePaths = paths.DuplicatePaths;
            PotentiallyShadowedFiles = paths.PotentiallyShadowedFiles;
            PotentiallyShadowedBytes = paths.PotentiallyShadowedBytes;
            TopShadowedMods = paths.TopShadowedMods
                .Select(item => new ShadowedModReport(item.PackageId, item.Files))
                .ToList();
        }
    }

    [DataContract]
    internal sealed class ShadowedModReport
    {
        [DataMember(Name = "packageId", Order = 1)]
        public string PackageId { get; private set; }

        [DataMember(Name = "files", Order = 2)]
        public int Files { get; private set; }

        internal ShadowedModReport(string packageId, int files)
        {
            PackageId = packageId;
            Files = files;
        }
    }

    [DataContract]
    internal sealed class TextureReport
    {
        [DataMember(Name = "files", Order = 1)] public long Files { get; private set; }
        [DataMember(Name = "bytes", Order = 2)] public long Bytes { get; private set; }
        [DataMember(Name = "totalMs", Order = 3)] public double TotalMilliseconds { get; private set; }
        [DataMember(Name = "readMs", Order = 4)] public double ReadMilliseconds { get; private set; }
        [DataMember(Name = "processingMs", Order = 5)] public double ProcessingMilliseconds { get; private set; }
        [DataMember(Name = "loadImageCalls", Order = 6)] public long LoadImageCalls { get; private set; }
        [DataMember(Name = "loadImageMs", Order = 7)] public double LoadImageMilliseconds { get; private set; }
        [DataMember(Name = "applyCalls", Order = 8)] public long ApplyCalls { get; private set; }
        [DataMember(Name = "applyMs", Order = 9)] public double ApplyMilliseconds { get; private set; }
        [DataMember(Name = "fastCompressCalls", Order = 10)] public long FastCompressCalls { get; private set; }
        [DataMember(Name = "fastCompressMs", Order = 11)] public double FastCompressMilliseconds { get; private set; }
        [DataMember(Name = "otherMs", Order = 12)] public double OtherMilliseconds { get; private set; }
        [DataMember(Name = "ddsFiles", Order = 13)] public long DdsFiles { get; private set; }
        [DataMember(Name = "ddsBytes", Order = 14)] public long DdsBytes { get; private set; }
        [DataMember(Name = "ddsMs", Order = 15)] public double DdsMilliseconds { get; private set; }

        internal TextureReport(TextureProbeSnapshot textures)
        {
            Files = textures.Files;
            Bytes = textures.Bytes;
            TotalMilliseconds = textures.TotalMilliseconds;
            ReadMilliseconds = textures.ReadMilliseconds;
            ProcessingMilliseconds = textures.ProcessingMilliseconds;
            LoadImageCalls = textures.LoadImageCalls;
            LoadImageMilliseconds = textures.LoadImageMilliseconds;
            ApplyCalls = textures.ApplyCalls;
            ApplyMilliseconds = textures.ApplyMilliseconds;
            FastCompressCalls = textures.FastCompressCalls;
            FastCompressMilliseconds = textures.FastCompressMilliseconds;
            OtherMilliseconds = textures.OtherMilliseconds;
            DdsFiles = textures.DdsFiles;
            DdsBytes = textures.DdsBytes;
            DdsMilliseconds = textures.DdsMilliseconds;
        }
    }

    [DataContract]
    internal sealed class DdsCacheReport
    {
        [DataMember(Name = "enabled", Order = 1)] public bool Enabled { get; private set; }
        [DataMember(Name = "hits", Order = 2)] public long Hits { get; private set; }
        [DataMember(Name = "misses", Order = 3)] public long Misses { get; private set; }
        [DataMember(Name = "created", Order = 4)] public long Created { get; private set; }
        [DataMember(Name = "invalidated", Order = 5)] public long Invalidated { get; private set; }
        [DataMember(Name = "excluded", Order = 6)] public long Excluded { get; private set; }
        [DataMember(Name = "unsupported", Order = 7)] public long Unsupported { get; private set; }
        [DataMember(Name = "budgetSkipped", Order = 8)] public long BudgetSkipped { get; private set; }
        [DataMember(Name = "failed", Order = 9)] public long Failed { get; private set; }
        [DataMember(Name = "buildMs", Order = 10)] public long BuildMilliseconds { get; private set; }
        [DataMember(Name = "cacheBytes", Order = 11)] public long CacheBytes { get; private set; }
        [DataMember(Name = "maxCacheBytes", Order = 12)] public long MaxCacheBytes { get; private set; }
        [DataMember(Name = "workerCount", Order = 13)] public int WorkerCount { get; private set; }
        [DataMember(Name = "workerPreparedMods", Order = 14)] public long WorkerPreparedMods { get; private set; }
        [DataMember(Name = "workerAppliedMods", Order = 15)] public long WorkerAppliedMods { get; private set; }
        [DataMember(Name = "workerFallbackMods", Order = 16)] public long WorkerFallbackMods { get; private set; }

        internal DdsCacheReport(TextureDdsCacheSnapshot cache)
        {
            Enabled = cache.Enabled;
            Hits = cache.Hits;
            Misses = cache.Misses;
            Created = cache.Created;
            Invalidated = cache.Invalidated;
            Excluded = cache.Excluded;
            Unsupported = cache.Unsupported;
            BudgetSkipped = cache.BudgetSkipped;
            Failed = cache.Failed;
            BuildMilliseconds = cache.BuildMilliseconds;
            CacheBytes = cache.CacheBytes;
            MaxCacheBytes = cache.MaxCacheBytes;
            WorkerCount = cache.WorkerCount;
            WorkerPreparedMods = cache.WorkerPreparedMods;
            WorkerAppliedMods = cache.WorkerAppliedMods;
            WorkerFallbackMods = cache.WorkerFallbackMods;
        }
    }
}
