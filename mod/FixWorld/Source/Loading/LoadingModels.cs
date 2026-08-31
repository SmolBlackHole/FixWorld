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
        CombineXml,
        ParseTranslationKeys,
        CheckPatches,
        ApplyPatches,
        ParseDefinitions,
        ClearPatchCache,
        LoadLanguageMetadata,
        CopyDefinitions,
        ResolveCrossReferences,
        RebindDefinitions,
        BuildLanguageMappings,
        GenerateImpliedDefinitions,
        ResolveDefinitions,
        InitializeRuntime,
        DelayedInitialization,
        LoadContent,
        LoadAudio,
        LoadTextures,
        LoadStrings,
        LoadAssetBundles,
        LoadBios,
        InjectLanguage,
        RunStaticConstructors,
        BakeAtlases,
        GarbageCollection
    }

    internal enum LoadingContentKind
    {
        None,
        Audio,
        Textures,
        Strings
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
        internal readonly int CompletedItems;
        internal readonly int TotalItems;
        internal readonly string ItemUnit;

        internal LoadingSnapshot(
            LoadingStage stage,
            string stageName,
            string stepName,
            double elapsedMilliseconds,
            float progress,
            bool hasDurationEstimate,
            double estimatedTotalMilliseconds,
            string activity,
            int completedItems,
            int totalItems,
            string itemUnit)
        {
            Stage = stage;
            StageName = stageName;
            StepName = stepName;
            ElapsedMilliseconds = elapsedMilliseconds;
            Progress = progress;
            HasDurationEstimate = hasDurationEstimate;
            EstimatedTotalMilliseconds = estimatedTotalMilliseconds;
            Activity = activity;
            CompletedItems = completedItems;
            TotalItems = totalItems;
            ItemUnit = itemUnit;
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

        internal LoadingMeasurement(
            double observedMilliseconds,
            IReadOnlyList<LoadingStepMeasurement> steps)
        {
            ObservedMilliseconds = observedMilliseconds;
            Steps = steps;
        }
    }
}
