extern alias FixWorldShared;

using SharedProfiling = FixWorldShared::FixWorld.Profiling;

namespace FixWorld.PlayData
{
    internal interface IPlayDataLoadObserver
    {
        void Observe(PlayDataLoadStageEvent stageEvent);
    }

    internal sealed class ProfilingPlayDataLoadObserver :
        IPlayDataLoadObserver
    {
        private readonly SharedProfiling.Profiler<PlayDataLoadStage> profiler =
            new SharedProfiling.Profiler<PlayDataLoadStage>();

        public void Observe(PlayDataLoadStageEvent stageEvent)
        {
            if (stageEvent.Kind == PlayDataLoadStageEventKind.Completed ||
                stageEvent.Kind == PlayDataLoadStageEventKind.Failed)
            {
                profiler.Observe(
                    stageEvent.Stage,
                    stageEvent.Elapsed,
                    stageEvent.Kind == PlayDataLoadStageEventKind.Completed);
            }
        }

        internal SharedProfiling.ProfileSnapshot<PlayDataLoadStage> Snapshot()
        {
            return profiler.Snapshot();
        }
    }
}
