using System;
using System.Diagnostics;

namespace FixWorld.PlayData
{
    internal sealed class PlayDataLoadContext
    {
        private readonly IPlayDataLoadObserver observer;
        private int nextSequence;

        internal PlayDataLoadContext(IPlayDataLoadObserver observer)
        {
            this.observer = observer ??
                throw new ArgumentNullException(nameof(observer));
        }

        internal void Run(PlayDataLoadStage stage, Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            int sequence = nextSequence++;
            observer.Observe(new PlayDataLoadStageEvent(
                sequence,
                stage,
                PlayDataLoadStageEventKind.Started,
                TimeSpan.Zero,
                null));
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                action();
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                observer.Observe(new PlayDataLoadStageEvent(
                    sequence,
                    stage,
                    PlayDataLoadStageEventKind.Failed,
                    stopwatch.Elapsed,
                    exception));
                throw;
            }

            stopwatch.Stop();
            observer.Observe(new PlayDataLoadStageEvent(
                sequence,
                stage,
                PlayDataLoadStageEventKind.Completed,
                stopwatch.Elapsed,
                null));
        }
    }
}
