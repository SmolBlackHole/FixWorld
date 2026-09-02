using System;

namespace FixWorld.PlayData
{
    internal enum PlayDataLoadStage
    {
        Reset = 1,
        InitializeMods = 2,
        IndexModContent = 3,
        PrepareTextureCache = 4,
        PrepareModContent = 5,
        CreateModClasses = 6,
        LoadAndPatchXml = 7,
        ImportDefinitions = 8,
        EarlyBinding = 9,
        PreResolveImpliedDefinitions = 10,
        CrossReferenceResolution = 11,
        ReferenceResolution = 12,
        PostResolveImpliedDefinitions = 13,
        DefinitionFinalization = 14,
        InitializeRuntime = 15,
        DeferredMainThreadWork = 16,
        Complete = 17
    }

    internal enum PlayDataLoadStageEventKind
    {
        Started,
        Progress,
        Completed,
        Failed
    }

    internal enum PlayDataLoadStageGroup
    {
        Boot = 1,
        Content = 2,
        Definitions = 3,
        Finalize = 4
    }

    internal sealed class PlayDataLoadStageEvent
    {
        internal PlayDataLoadStageEvent(
            PlayDataLoadStage stage,
            PlayDataLoadStageEventKind kind,
            TimeSpan elapsed,
            string activity,
            PlayDataStageDiagnostics diagnostics)
        {
            Stage = stage;
            Kind = kind;
            Elapsed = elapsed;
            Activity = activity;
            Diagnostics = diagnostics;
        }

        internal PlayDataLoadStage Stage { get; }

        internal PlayDataLoadStageEventKind Kind { get; }

        internal TimeSpan Elapsed { get; }

        internal string Activity { get; }

        internal PlayDataStageDiagnostics Diagnostics { get; }
    }

    internal readonly struct PlayDataStageDiagnostics
    {
        internal PlayDataStageDiagnostics(
            bool resourceMetricsAvailable,
            bool mainThread,
            int managedThreadId,
            TimeSpan processCpuTime,
            long managedHeapDeltaBytes,
            long workingSetDeltaBytes,
            int generationZeroCollections,
            int generationOneCollections,
            int generationTwoCollections)
        {
            ResourceMetricsAvailable = resourceMetricsAvailable;
            MainThread = mainThread;
            ManagedThreadId = managedThreadId;
            ProcessCpuTime = processCpuTime;
            ManagedHeapDeltaBytes = managedHeapDeltaBytes;
            WorkingSetDeltaBytes = workingSetDeltaBytes;
            GenerationZeroCollections = generationZeroCollections;
            GenerationOneCollections = generationOneCollections;
            GenerationTwoCollections = generationTwoCollections;
        }

        internal bool ResourceMetricsAvailable { get; }

        internal bool MainThread { get; }

        internal int ManagedThreadId { get; }

        internal TimeSpan ProcessCpuTime { get; }

        internal long ManagedHeapDeltaBytes { get; }

        internal long WorkingSetDeltaBytes { get; }

        internal int GenerationZeroCollections { get; }

        internal int GenerationOneCollections { get; }

        internal int GenerationTwoCollections { get; }
    }

    internal static class PlayDataLoadStageCatalog
    {
        internal const int Count = (int)PlayDataLoadStage.Complete;
        internal const int GroupCount = (int)PlayDataLoadStageGroup.Finalize;

        internal static PlayDataLoadStageGroup GetGroup(
            PlayDataLoadStage stage)
        {
            if (stage <= PlayDataLoadStage.InitializeMods)
            {
                return PlayDataLoadStageGroup.Boot;
            }

            if (stage <= PlayDataLoadStage.CreateModClasses)
            {
                return PlayDataLoadStageGroup.Content;
            }

            if (stage <= PlayDataLoadStage.DefinitionFinalization)
            {
                return PlayDataLoadStageGroup.Definitions;
            }

            return PlayDataLoadStageGroup.Finalize;
        }

        internal static string GetGroupName(PlayDataLoadStageGroup group)
        {
            switch (group)
            {
                case PlayDataLoadStageGroup.Boot:
                    return "Boot";
                case PlayDataLoadStageGroup.Content:
                    return "Content";
                case PlayDataLoadStageGroup.Definitions:
                    return "Definitions";
                case PlayDataLoadStageGroup.Finalize:
                    return "Finalize";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(group),
                        group,
                        null);
            }
        }

        internal static PlayDataLoadStage GetFirstStage(
            PlayDataLoadStageGroup group)
        {
            switch (group)
            {
                case PlayDataLoadStageGroup.Boot:
                    return PlayDataLoadStage.Reset;
                case PlayDataLoadStageGroup.Content:
                    return PlayDataLoadStage.IndexModContent;
                case PlayDataLoadStageGroup.Definitions:
                    return PlayDataLoadStage.LoadAndPatchXml;
                case PlayDataLoadStageGroup.Finalize:
                    return PlayDataLoadStage.InitializeRuntime;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(group),
                        group,
                        null);
            }
        }

        internal static PlayDataLoadStage GetLastStage(
            PlayDataLoadStageGroup group)
        {
            switch (group)
            {
                case PlayDataLoadStageGroup.Boot:
                    return PlayDataLoadStage.InitializeMods;
                case PlayDataLoadStageGroup.Content:
                    return PlayDataLoadStage.CreateModClasses;
                case PlayDataLoadStageGroup.Definitions:
                    return PlayDataLoadStage.DefinitionFinalization;
                case PlayDataLoadStageGroup.Finalize:
                    return PlayDataLoadStage.Complete;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(group),
                        group,
                        null);
            }
        }

        internal static int GetIndexInGroup(PlayDataLoadStage stage)
        {
            return (int)stage - (int)GetFirstStage(GetGroup(stage)) + 1;
        }

        internal static int GetGroupStageCount(PlayDataLoadStageGroup group)
        {
            return (int)GetLastStage(group) - (int)GetFirstStage(group) + 1;
        }

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
                case PlayDataLoadStage.PrepareTextureCache:
                    return "Prepare texture cache";
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
                case PlayDataLoadStage.PrepareTextureCache:
                    return "DDS";
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
