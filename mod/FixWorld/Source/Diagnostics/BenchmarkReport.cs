using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using FixWorld.Textures;
using FixWorld.Loading;

namespace FixWorld.Diagnostics
{
    [DataContract]
    internal sealed class BenchmarkReport
    {
        private const int CurrentSchemaVersion = 4;

        [DataMember(Name = "schemaVersion", Order = 1)]
        public int SchemaVersion { get; private set; }

        [DataMember(Name = "completedUtc", Order = 2)]
        public string CompletedUtc { get; private set; }

        [DataMember(Name = "completion", Order = 3)]
        public CompletionReport Completion { get; private set; }

        [DataMember(Name = "loader", Order = 4)]
        public LoaderReport Loader { get; private set; }

        [DataMember(Name = "files", Order = 5)]
        public FileDiscoveryReport Files { get; private set; }

        [DataMember(Name = "texturePaths", Order = 6)]
        public TexturePathReport TexturePaths { get; private set; }

        [DataMember(Name = "textures", Order = 7)]
        public TextureReport Textures { get; private set; }

        [DataMember(Name = "ddsCache", Order = 8)]
        public DdsCacheReport DdsCache { get; private set; }

        private BenchmarkReport()
        {
        }

        internal static BenchmarkReport Create(
            string completionSource,
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
                Completion = new CompletionReport(completionSource),
                Loader = new LoaderReport(
                    loading.ObservedMilliseconds,
                    stages,
                    steps,
                    loading.DelayedActions
                        .Select(item => new DelayedActionReport(item))
                        .ToList(),
                    loading.StaticConstructors
                        .Select(item => new StaticConstructorReport(item))
                        .ToList(),
                    loading.StaticConstructorTailMilliseconds,
                    loading.Mods.Select(item => new ModLoadingReport(item)).ToList(),
                    loading.Overhead
                        .Select(item => new LoadingOverheadReport(item))
                        .ToList()),
                Files = new FileDiscoveryReport(files),
                TexturePaths = new TexturePathReport(texturePaths),
                Textures = new TextureReport(textures),
                DdsCache = new DdsCacheReport(ddsCache)
            };
        }

        internal void Write(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    "The benchmark output path has no parent directory: " + fullPath);
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(BenchmarkReport));
                using (FileStream stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    serializer.WriteObject(stream, this);
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(temporaryPath, fullPath, null);
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
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

        [DataMember(Name = "delayedActions", Order = 4)]
        public List<DelayedActionReport> DelayedActions { get; private set; }

        [DataMember(Name = "staticConstructors", Order = 5)]
        public List<StaticConstructorReport> StaticConstructors { get; private set; }

        [DataMember(Name = "staticConstructorTailMs", Order = 6)]
        public double StaticConstructorTailMilliseconds { get; private set; }

        [DataMember(Name = "mods", Order = 7)]
        public List<ModLoadingReport> Mods { get; private set; }

        [DataMember(Name = "overhead", Order = 8)]
        public List<LoadingOverheadReport> Overhead { get; private set; }

        internal LoaderReport(
            double observedMilliseconds,
            List<LoaderStageReport> stages,
            List<LoaderStepReport> steps,
            List<DelayedActionReport> delayedActions,
            List<StaticConstructorReport> staticConstructors,
            double staticConstructorTailMilliseconds,
            List<ModLoadingReport> mods,
            List<LoadingOverheadReport> overhead)
        {
            ObservedMilliseconds = observedMilliseconds;
            Stages = stages;
            Steps = steps;
            DelayedActions = delayedActions;
            StaticConstructors = staticConstructors;
            StaticConstructorTailMilliseconds = staticConstructorTailMilliseconds;
            Mods = mods;
            Overhead = overhead;
        }
    }

    [DataContract]
    internal sealed class DelayedActionReport
    {
        [DataMember(Name = "method", Order = 1)]
        public string Method { get; private set; }

        [DataMember(Name = "packageId", Order = 2)]
        public string PackageId { get; private set; }

        [DataMember(Name = "mod", Order = 3)]
        public string ModName { get; private set; }

        [DataMember(Name = "calls", Order = 4)]
        public long Calls { get; private set; }

        [DataMember(Name = "totalMs", Order = 5)]
        public double TotalMilliseconds { get; private set; }

        [DataMember(Name = "maxMs", Order = 6)]
        public double MaxMilliseconds { get; private set; }

        internal DelayedActionReport(DelayedActionSnapshot action)
        {
            Method = action.Method;
            PackageId = action.PackageId;
            ModName = action.ModName;
            Calls = action.Calls;
            TotalMilliseconds = action.TotalMilliseconds;
            MaxMilliseconds = action.MaxMilliseconds;
        }
    }

    [DataContract]
    internal sealed class StaticConstructorReport
    {
        [DataMember(Name = "type", Order = 1)]
        public string TypeName { get; private set; }

        [DataMember(Name = "packageId", Order = 2)]
        public string PackageId { get; private set; }

        [DataMember(Name = "mod", Order = 3)]
        public string ModName { get; private set; }

        [DataMember(Name = "calls", Order = 4)]
        public long Calls { get; private set; }

        [DataMember(Name = "totalMs", Order = 5)]
        public double TotalMilliseconds { get; private set; }

        [DataMember(Name = "maxMs", Order = 6)]
        public double MaxMilliseconds { get; private set; }

        [DataMember(Name = "failures", Order = 7)]
        public long Failures { get; private set; }

        internal StaticConstructorReport(StaticConstructorSnapshot constructor)
        {
            TypeName = constructor.TypeName;
            PackageId = constructor.PackageId;
            ModName = constructor.ModName;
            Calls = constructor.Calls;
            TotalMilliseconds = constructor.TotalMilliseconds;
            MaxMilliseconds = constructor.MaxMilliseconds;
            Failures = constructor.Failures;
        }
    }

    [DataContract]
    internal sealed class ModLoadingReport
    {
        [DataMember(Name = "packageId", Order = 1)]
        public string PackageId { get; private set; }

        [DataMember(Name = "mod", Order = 2)]
        public string ModName { get; private set; }

        [DataMember(Name = "attribution", Order = 3)]
        public string Attribution { get; private set; }

        [DataMember(Name = "stage", Order = 4)]
        public string Stage { get; private set; }

        [DataMember(Name = "operation", Order = 5)]
        public string Operation { get; private set; }

        [DataMember(Name = "calls", Order = 6)]
        public long Calls { get; private set; }

        [DataMember(Name = "failures", Order = 7)]
        public long Failures { get; private set; }

        [DataMember(Name = "executionMs", Order = 8)]
        public double ExecutionMilliseconds { get; private set; }

        [DataMember(Name = "mainThreadMs", Order = 9)]
        public double MainThreadMilliseconds { get; private set; }

        [DataMember(Name = "workerThreadMs", Order = 10)]
        public double WorkerThreadMilliseconds { get; private set; }

        [DataMember(Name = "waitMs", Order = 11)]
        public double WaitMilliseconds { get; private set; }

        [DataMember(Name = "wallMs", Order = 12)]
        public double WallMilliseconds { get; private set; }

        internal ModLoadingReport(ModLoadingMeasurement measurement)
        {
            PackageId = measurement.PackageId;
            ModName = measurement.ModName;
            Attribution = measurement.Attribution.ToString();
            Stage = LoadingStageNames.GetName(measurement.Stage);
            Operation = measurement.Operation.ToString();
            Calls = measurement.Calls;
            Failures = measurement.Failures;
            ExecutionMilliseconds = measurement.ExecutionMilliseconds;
            MainThreadMilliseconds = measurement.MainThreadMilliseconds;
            WorkerThreadMilliseconds = measurement.WorkerThreadMilliseconds;
            WaitMilliseconds = measurement.WaitMilliseconds;
            WallMilliseconds = measurement.WallMilliseconds;
        }
    }

    [DataContract]
    internal sealed class LoadingOverheadReport
    {
        [DataMember(Name = "operation", Order = 1)]
        public string Operation { get; private set; }

        [DataMember(Name = "calls", Order = 2)]
        public long Calls { get; private set; }

        [DataMember(Name = "totalMs", Order = 3)]
        public double TotalMilliseconds { get; private set; }

        [DataMember(Name = "maxMs", Order = 4)]
        public double MaxMilliseconds { get; private set; }

        [DataMember(Name = "estimated", Order = 5)]
        public bool Estimated { get; private set; }

        internal LoadingOverheadReport(LoadingOverheadMeasurement measurement)
        {
            Operation = measurement.Kind.ToString();
            Calls = measurement.Calls;
            TotalMilliseconds = measurement.TotalMilliseconds;
            MaxMilliseconds = measurement.MaxMilliseconds;
            Estimated = measurement.Estimated;
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
