using System;
using System.Collections.Generic;

namespace FixWorld.Loading
{
    internal enum LoadingStage
    {
        Bootstrap = 1,
        XmlAndPatches = 2,
        Definitions = 3,
        Content = 4,
        Finalize = 5
    }

    internal enum LoadingStep
    {
        LoadXml,
        DiscoverXml,
        ParseXml,
        CommitXml,
        CombineXml,
        ParseTranslationKeys,
        CheckPatches,
        ApplyPatches,
        ParseDefinitions,
        LoadPatchFiles,
        RegisterXmlInheritance,
        ResolveXmlInheritance,
        MaterializeDefinitions,
        ClearPatchCache,
        LoadLanguageMetadata,
        CopyDefinitions,
        ResolveCrossReferences,
        RebindDefinitions,
        BuildLanguageMappings,
        GenerateImpliedDefinitions,
        ResolveDefinitions,
        LoadKeyboardPreferences,
        AssignDefinitionIds,
        DelayedInitialization,
        LoadContent,
        LoadAudio,
        LoadTextures,
        LoadStrings,
        LoadAssetBundles,
        LoadBios,
        InjectLanguage,
        LoadTextureCache,
        ValidateTextureCache,
        CommitTextureCache,
        BuildTextureCache,
        PruneTextureCache,
        InitializeInterface,
        RunStaticConstructors,
        FinalizeStaticInitialization,
        CheckStaticConstructorAttributes,
        InitializeFloatMenus,
        BakeAtlases,
        GarbageCollection
    }

    internal enum LoadingOverheadKind
    {
        Classification,
        Scheduling,
        Telemetry
    }

    internal enum LoadingStageEventSource
    {
        RimWorld,
        FixWorld
    }

    internal static class LoadingStageNames
    {
        internal static string GetName(LoadingStage stage)
        {
            switch (stage)
            {
                case LoadingStage.Bootstrap: return "Bootstrap";
                case LoadingStage.XmlAndPatches: return "XML & patches";
                case LoadingStage.Definitions: return "Definitions";
                case LoadingStage.Content: return "Content";
                case LoadingStage.Finalize: return "Finalize";
                default: throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        internal static string GetFallback(LoadingStage stage)
        {
            switch (stage)
            {
                case LoadingStage.Bootstrap: return "Preparing the mod environment";
                case LoadingStage.XmlAndPatches: return "Processing XML and patches";
                case LoadingStage.Definitions: return "Preparing game definitions";
                case LoadingStage.Content: return "Preparing mod content";
                case LoadingStage.Finalize: return "Finalizing startup";
                default: throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }
    }

    internal readonly struct LoadingSnapshot
    {
        internal readonly LoadingStage Stage;
        internal readonly string StageName;
        internal readonly string StepName;
        internal readonly double ElapsedMilliseconds;
        internal readonly float Progress;
        internal readonly bool HasDurationEstimate;
        internal readonly double EstimatedTotalMilliseconds;
        internal readonly string Activity;
        internal readonly LoadingStageEventSource Source;

        internal LoadingSnapshot(
            LoadingStage stage,
            string stageName,
            string stepName,
            double elapsedMilliseconds,
            float progress,
            bool hasDurationEstimate,
            double estimatedTotalMilliseconds,
            string activity,
            LoadingStageEventSource source)
        {
            Stage = stage;
            StageName = stageName;
            StepName = stepName;
            ElapsedMilliseconds = elapsedMilliseconds;
            Progress = progress;
            HasDurationEstimate = hasDurationEstimate;
            EstimatedTotalMilliseconds = estimatedTotalMilliseconds;
            Activity = activity;
            Source = source;
        }
    }

    internal sealed class LoadingStepMeasurement
    {
        internal LoadingStep Step { get; }
        internal LoadingStage Stage { get; }
        internal string Name { get; }
        internal long Calls { get; }
        internal double TotalMilliseconds { get; }
        internal double ExclusiveMilliseconds { get; }
        internal double MainThreadMilliseconds { get; }
        internal double WorkerThreadMilliseconds { get; }
        internal double MainThreadExclusiveMilliseconds { get; }
        internal double WorkerThreadExclusiveMilliseconds { get; }

        internal LoadingStepMeasurement(
            LoadingStep step,
            LoadingStage stage,
            string name,
            long calls,
            double totalMilliseconds,
            double exclusiveMilliseconds,
            double mainThreadMilliseconds,
            double workerThreadMilliseconds,
            double mainThreadExclusiveMilliseconds,
            double workerThreadExclusiveMilliseconds)
        {
            Step = step;
            Stage = stage;
            Name = name;
            Calls = calls;
            TotalMilliseconds = totalMilliseconds;
            ExclusiveMilliseconds = exclusiveMilliseconds;
            MainThreadMilliseconds = mainThreadMilliseconds;
            WorkerThreadMilliseconds = workerThreadMilliseconds;
            MainThreadExclusiveMilliseconds = mainThreadExclusiveMilliseconds;
            WorkerThreadExclusiveMilliseconds = workerThreadExclusiveMilliseconds;
        }
    }

    internal sealed class LoadingMeasurement
    {
        internal double ObservedMilliseconds { get; }
        internal IReadOnlyList<LoadingStepMeasurement> Steps { get; }
        internal IReadOnlyList<DelayedActionSnapshot> DelayedActions { get; }
        internal IReadOnlyList<StaticConstructorSnapshot> StaticConstructors { get; }
        internal double StaticConstructorTailMilliseconds { get; }
        internal IReadOnlyList<ModLoadingMeasurement> Mods { get; }
        internal IReadOnlyList<LoadingOverheadMeasurement> Overhead { get; }

        internal LoadingMeasurement(
            double observedMilliseconds,
            IReadOnlyList<LoadingStepMeasurement> steps,
            IReadOnlyList<DelayedActionSnapshot> delayedActions,
            IReadOnlyList<StaticConstructorSnapshot> staticConstructors,
            double staticConstructorTailMilliseconds,
            IReadOnlyList<ModLoadingMeasurement> mods,
            IReadOnlyList<LoadingOverheadMeasurement> overhead)
        {
            ObservedMilliseconds = observedMilliseconds;
            Steps = steps;
            DelayedActions = delayedActions;
            StaticConstructors = staticConstructors;
            StaticConstructorTailMilliseconds = staticConstructorTailMilliseconds;
            Mods = mods;
            Overhead = overhead;
        }
    }

    internal readonly struct DelayedActionSnapshot
    {
        internal readonly string Method;
        internal readonly string PackageId;
        internal readonly string ModName;
        internal readonly long Calls;
        internal readonly double TotalMilliseconds;
        internal readonly double MaxMilliseconds;

        internal DelayedActionSnapshot(
            string method,
            string packageId,
            string modName,
            long calls,
            double totalMilliseconds,
            double maxMilliseconds)
        {
            Method = method;
            PackageId = packageId;
            ModName = modName;
            Calls = calls;
            TotalMilliseconds = totalMilliseconds;
            MaxMilliseconds = maxMilliseconds;
        }
    }

    internal readonly struct StaticConstructorSnapshot
    {
        internal readonly string TypeName;
        internal readonly string PackageId;
        internal readonly string ModName;
        internal readonly long Calls;
        internal readonly double TotalMilliseconds;
        internal readonly double MaxMilliseconds;
        internal readonly long Failures;

        internal StaticConstructorSnapshot(
            string typeName,
            string packageId,
            string modName,
            long calls,
            double totalMilliseconds,
            double maxMilliseconds,
            long failures)
        {
            TypeName = typeName;
            PackageId = packageId;
            ModName = modName;
            Calls = calls;
            TotalMilliseconds = totalMilliseconds;
            MaxMilliseconds = maxMilliseconds;
            Failures = failures;
        }
    }

    internal sealed class ModLoadingMeasurement
    {
        internal string PackageId { get; }
        internal string ModName { get; }
        internal ModAttributionQuality Attribution { get; }
        internal LoadingStage Stage { get; }
        internal LoadingStep Operation { get; }
        internal long Calls { get; }
        internal long Failures { get; }
        internal double ExecutionMilliseconds { get; }
        internal double MainThreadMilliseconds { get; }
        internal double WorkerThreadMilliseconds { get; }
        internal double WaitMilliseconds { get; }
        internal double WallMilliseconds { get; }

        internal ModLoadingMeasurement(
            string packageId,
            string modName,
            ModAttributionQuality attribution,
            LoadingStage stage,
            LoadingStep operation,
            long calls,
            long failures,
            double executionMilliseconds,
            double mainThreadMilliseconds,
            double workerThreadMilliseconds,
            double waitMilliseconds,
            double wallMilliseconds)
        {
            PackageId = packageId;
            ModName = modName;
            Attribution = attribution;
            Stage = stage;
            Operation = operation;
            Calls = calls;
            Failures = failures;
            ExecutionMilliseconds = executionMilliseconds;
            MainThreadMilliseconds = mainThreadMilliseconds;
            WorkerThreadMilliseconds = workerThreadMilliseconds;
            WaitMilliseconds = waitMilliseconds;
            WallMilliseconds = wallMilliseconds;
        }
    }

    internal sealed class LoadingOverheadMeasurement
    {
        internal LoadingOverheadKind Kind { get; }
        internal long Calls { get; }
        internal double TotalMilliseconds { get; }
        internal double MaxMilliseconds { get; }
        internal bool Estimated { get; }

        internal LoadingOverheadMeasurement(
            LoadingOverheadKind kind,
            long calls,
            double totalMilliseconds,
            double maxMilliseconds,
            bool estimated)
        {
            Kind = kind;
            Calls = calls;
            TotalMilliseconds = totalMilliseconds;
            MaxMilliseconds = maxMilliseconds;
            Estimated = estimated;
        }
    }
}
