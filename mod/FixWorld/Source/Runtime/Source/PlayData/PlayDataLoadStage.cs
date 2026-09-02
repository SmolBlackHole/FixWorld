using System;
using FixWorld.Loading;

namespace FixWorld.PlayData
{
    internal enum PlayDataLoadStage
    {
        Reset = 1,
        InitializeMods = 2,
        PrepareModContent = 3,
        CreateModClasses = 4,
        LoadAndPatchXml = 5,
        ImportDefinitions = 6,
        EarlyBinding = 7,
        PreResolveImpliedDefinitions = 8,
        CrossReferenceResolution = 9,
        ReferenceResolution = 10,
        PostResolveImpliedDefinitions = 11,
        DefinitionFinalization = 12,
        InitializeRuntime = 13,
        DeferredMainThreadWork = 14,
        Complete = 15
    }

    internal enum PlayDataLoadStageEventKind
    {
        Started,
        Progress,
        Completed,
        Failed
    }

    internal sealed class PlayDataLoadStageEvent
    {
        internal PlayDataLoadStageEvent(
            int sequence,
            PlayDataLoadStage stage,
            PlayDataLoadStageEventKind kind,
            TimeSpan elapsed,
            string activity,
            int completed,
            int total,
            Exception error)
        {
            Sequence = sequence;
            Stage = stage;
            Kind = kind;
            Elapsed = elapsed;
            Activity = activity;
            Completed = completed;
            Total = total;
            Error = error;
        }

        internal int Sequence { get; }

        internal PlayDataLoadStage Stage { get; }

        internal PlayDataLoadStageEventKind Kind { get; }

        internal TimeSpan Elapsed { get; }

        internal string Activity { get; }

        internal int Completed { get; }

        internal int Total { get; }

        internal Exception Error { get; }
    }

    internal static class PlayDataLoadStageCatalog
    {
        internal const int Count = 15;

        internal static string GetName(PlayDataLoadStage stage)
        {
            switch (stage)
            {
                case PlayDataLoadStage.Reset:
                    return "Reset play data";
                case PlayDataLoadStage.InitializeMods:
                    return "Initialize mods";
                case PlayDataLoadStage.PrepareModContent:
                    return "Prepare mod content";
                case PlayDataLoadStage.CreateModClasses:
                    return "Create mod classes";
                case PlayDataLoadStage.LoadAndPatchXml:
                    return "Load and patch XML";
                case PlayDataLoadStage.ImportDefinitions:
                    return "Import definitions";
                case PlayDataLoadStage.EarlyBinding:
                    return "Early binding";
                case PlayDataLoadStage.PreResolveImpliedDefinitions:
                    return "Generate pre-resolve definitions";
                case PlayDataLoadStage.CrossReferenceResolution:
                    return "Resolve cross-references";
                case PlayDataLoadStage.ReferenceResolution:
                    return "Resolve definitions";
                case PlayDataLoadStage.PostResolveImpliedDefinitions:
                    return "Generate post-resolve definitions";
                case PlayDataLoadStage.DefinitionFinalization:
                    return "Finalize definitions";
                case PlayDataLoadStage.InitializeRuntime:
                    return "Initialize runtime";
                case PlayDataLoadStage.DeferredMainThreadWork:
                    return "Execute deferred main-thread work";
                case PlayDataLoadStage.Complete:
                    return "Complete";
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        internal static string GetShortName(PlayDataLoadStage stage)
        {
            switch (stage)
            {
                case PlayDataLoadStage.Reset:
                    return "Reset";
                case PlayDataLoadStage.InitializeMods:
                    return "Mods";
                case PlayDataLoadStage.PrepareModContent:
                    return "Content";
                case PlayDataLoadStage.CreateModClasses:
                    return "Classes";
                case PlayDataLoadStage.LoadAndPatchXml:
                    return "XML";
                case PlayDataLoadStage.ImportDefinitions:
                    return "Import";
                case PlayDataLoadStage.EarlyBinding:
                    return "Bind";
                case PlayDataLoadStage.PreResolveImpliedDefinitions:
                    return "Pre-implied";
                case PlayDataLoadStage.CrossReferenceResolution:
                    return "Cross refs";
                case PlayDataLoadStage.ReferenceResolution:
                    return "Resolve";
                case PlayDataLoadStage.PostResolveImpliedDefinitions:
                    return "Post-implied";
                case PlayDataLoadStage.DefinitionFinalization:
                    return "Defs done";
                case PlayDataLoadStage.InitializeRuntime:
                    return "Runtime";
                case PlayDataLoadStage.DeferredMainThreadWork:
                    return "Deferred";
                case PlayDataLoadStage.Complete:
                    return "Ready";
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        internal static LoadingStage GetReportStage(PlayDataLoadStage stage)
        {
            if (stage <= PlayDataLoadStage.InitializeMods)
            {
                return LoadingStage.Bootstrap;
            }

            if (stage <= PlayDataLoadStage.CreateModClasses)
            {
                return LoadingStage.ModPreparation;
            }

            if (stage == PlayDataLoadStage.LoadAndPatchXml)
            {
                return LoadingStage.XmlAndPatches;
            }

            if (stage <= PlayDataLoadStage.InitializeRuntime)
            {
                return LoadingStage.Definitions;
            }

            return stage == PlayDataLoadStage.DeferredMainThreadWork
                ? LoadingStage.Content
                : LoadingStage.Finalize;
        }
    }
}
