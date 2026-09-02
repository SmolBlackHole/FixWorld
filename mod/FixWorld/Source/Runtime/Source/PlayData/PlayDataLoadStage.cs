using System;

namespace FixWorld.PlayData
{
    internal enum PlayDataLoadStage
    {
        Reset = 1,
        InitializeMods = 2,
        IndexModContent = 3,
        PrepareModContent = 4,
        CreateModClasses = 5,
        LoadAndPatchXml = 6,
        ImportDefinitions = 7,
        EarlyBinding = 8,
        PreResolveImpliedDefinitions = 9,
        CrossReferenceResolution = 10,
        ReferenceResolution = 11,
        PostResolveImpliedDefinitions = 12,
        DefinitionFinalization = 13,
        InitializeRuntime = 14,
        DeferredMainThreadWork = 15,
        Complete = 16
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
            PlayDataLoadStage stage,
            PlayDataLoadStageEventKind kind,
            TimeSpan elapsed,
            string activity)
        {
            Stage = stage;
            Kind = kind;
            Elapsed = elapsed;
            Activity = activity;
        }

        internal PlayDataLoadStage Stage { get; }

        internal PlayDataLoadStageEventKind Kind { get; }

        internal TimeSpan Elapsed { get; }

        internal string Activity { get; }
    }

    internal static class PlayDataLoadStageCatalog
    {
        internal const int Count = (int)PlayDataLoadStage.Complete;

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
                case PlayDataLoadStage.IndexModContent:
                    return "Index mod content";
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
                case PlayDataLoadStage.IndexModContent:
                    return "Index";
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
    }
}
