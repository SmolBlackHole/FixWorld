using System;
using System.Diagnostics;
using System.Threading;
using FixWorld.Events;
using Verse;

namespace FixWorld.PlayData
{
    internal sealed class PlayDataStageRunner
    {
        private readonly EventBus events;

        internal PlayDataStageRunner(EventBus events)
        {
            this.events = events ?? throw new ArgumentNullException(nameof(events));
        }

        internal void Run(PlayDataLoadStage stage, Action execute)
        {
            Run(
                stage,
                () =>
                {
                    execute();
                    return false;
                });
        }

        internal TResult Run<TResult>(
            PlayDataLoadStage stage,
            Func<TResult> execute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            using (PlayDataStageOperation operation = Begin(stage))
            {
                try
                {
                    TResult result = execute();
                    operation.Complete();
                    return result;
                }
                catch
                {
                    operation.Fail();
                    throw;
                }
            }
        }

        internal PlayDataStageOperation Begin(PlayDataLoadStage stage)
        {
            LongEventHandler.SetCurrentEventText(
                "FixWorld: " + PlayDataLoadStageCatalog.GetName(stage));
            return new PlayDataStageOperation(events, stage);
        }
    }

    internal sealed class PlayDataStageOperation : IDisposable
    {
        private readonly EventBus events;
        private readonly Stopwatch stopwatch;
        private int terminal;

        internal PlayDataStageOperation(
            EventBus events,
            PlayDataLoadStage stage)
        {
            this.events = events;
            Stage = stage;
            stopwatch = Stopwatch.StartNew();
            Publish(PlayDataLoadStageEventKind.Started, null);
        }

        internal PlayDataLoadStage Stage { get; }

        internal void Report(string activity)
        {
            if (Volatile.Read(ref terminal) == 0)
            {
                events.PublishLatest(
                    "play-data-progress",
                    Create(PlayDataLoadStageEventKind.Progress, activity));
            }
        }

        internal void Complete()
        {
            Finish(PlayDataLoadStageEventKind.Completed);
        }

        internal void Fail()
        {
            Finish(PlayDataLoadStageEventKind.Failed);
        }

        public void Dispose()
        {
            Complete();
        }

        private void Finish(PlayDataLoadStageEventKind kind)
        {
            if (Interlocked.Exchange(ref terminal, 1) != 0)
            {
                return;
            }

            stopwatch.Stop();
            Publish(kind, null);
        }

        private void Publish(
            PlayDataLoadStageEventKind kind,
            string activity)
        {
            events.Publish(Create(kind, activity));
        }

        private PlayDataLoadStageEvent Create(
            PlayDataLoadStageEventKind kind,
            string activity)
        {
            return new PlayDataLoadStageEvent(
                Stage,
                kind,
                stopwatch.Elapsed,
                activity);
        }
    }
}
