using System;
using System.Collections.Generic;

namespace FixWorld.Loading
{
    internal enum LoadingStage
    {
        Bootstrap = 1,
        ModPreparation = 2,
        XmlAndPatches = 3,
        Definitions = 4,
        Content = 5,
        Finalize = 6
    }

    internal enum LoadingStep
    {
        ResetPlayData,
        InitializeMods,
        IndexModContent,
        PrepareModContent,
        CreateModClasses,
        LoadAndPatchXml,
        ImportDefinitions,
        EarlyBinding,
        PreResolveImpliedDefinitions,
        CrossReferenceResolution,
        ReferenceResolution,
        PostResolveImpliedDefinitions,
        DefinitionFinalization,
        InitializeRuntime,
        DeferredMainThreadWork,
        CompletePlayData
    }

    internal static class LoadingStageNames
    {
        internal static string GetName(LoadingStage stage)
        {
            switch (stage)
            {
                case LoadingStage.Bootstrap:
                    return "Bootstrap";
                case LoadingStage.ModPreparation:
                    return "Mod preparation";
                case LoadingStage.XmlAndPatches:
                    return "XML & patches";
                case LoadingStage.Definitions:
                    return "Definitions";
                case LoadingStage.Content:
                    return "Content";
                case LoadingStage.Finalize:
                    return "Finalize";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(stage),
                        stage,
                        null);
            }
        }
    }

    internal sealed class LoadingStepMeasurement
    {
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
            MainThreadExclusiveMilliseconds =
                mainThreadExclusiveMilliseconds;
            WorkerThreadExclusiveMilliseconds =
                workerThreadExclusiveMilliseconds;
        }

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
    }

    internal sealed class LoadingMeasurement
    {
        internal LoadingMeasurement(
            double observedMilliseconds,
            IReadOnlyList<LoadingStepMeasurement> steps)
        {
            ObservedMilliseconds = observedMilliseconds;
            Steps = steps;
        }

        internal double ObservedMilliseconds { get; }
        internal IReadOnlyList<LoadingStepMeasurement> Steps { get; }
    }
}
