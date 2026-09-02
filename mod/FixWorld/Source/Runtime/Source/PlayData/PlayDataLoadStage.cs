using System;

namespace FixWorld.PlayData
{
    internal enum PlayDataLoadStage
    {
        Reset,
        ModBoot,
        LanguageMetadata,
        DefinitionImport,
        EarlyBinding,
        PreResolveImpliedDefinitions,
        CrossReferenceResolution,
        ReferenceResolution,
        PostResolveImpliedDefinitions,
        DefinitionFinalization,
        Initialization,
        DeferredInitialization
    }

    internal enum PlayDataLoadStageEventKind
    {
        Started,
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
            Exception error)
        {
            Sequence = sequence;
            Stage = stage;
            Kind = kind;
            Elapsed = elapsed;
            Error = error;
        }

        internal int Sequence { get; }

        internal PlayDataLoadStage Stage { get; }

        internal PlayDataLoadStageEventKind Kind { get; }

        internal TimeSpan Elapsed { get; }

        internal Exception Error { get; }
    }
}
