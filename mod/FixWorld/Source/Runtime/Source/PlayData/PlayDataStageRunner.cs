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
        private int nextSequence;

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
                catch (Exception exception)
                {
                    operation.Fail(exception);
                    throw;
                }
            }
        }

        internal PlayDataStageOperation Begin(PlayDataLoadStage stage)
        {
            int sequence = Interlocked.Increment(ref nextSequence);
            LongEventHandler.SetCurrentEventText(
                "FixWorld: " + PlayDataLoadStageCatalog.GetName(stage));
            return new PlayDataStageOperation(events, sequence, stage);
        }
    }

    internal sealed class PlayDataStageOperation : IDisposable
    {
        private readonly EventBus events;
        private readonly Stopwatch stopwatch;
        private int terminal;

        internal PlayDataStageOperation(
            EventBus events,
            int sequence,
            PlayDataLoadStage stage)
        {
            this.events = events;
            Sequence = sequence;
            Stage = stage;
            stopwatch = Stopwatch.StartNew();
            Publish(PlayDataLoadStageEventKind.Started, null, 0, 0, null);
        }

        internal int Sequence { get; }

        internal PlayDataLoadStage Stage { get; }

        internal void Report(string activity, int completed, int total)
        {
            if (Volatile.Read(ref terminal) == 0)
            {
                events.PublishLatest(
                    "play-data-progress",
                    Create(
                        PlayDataLoadStageEventKind.Progress,
                        activity,
                        completed,
                        total,
                        null));
            }
        }

        internal void Complete()
        {
            Finish(PlayDataLoadStageEventKind.Completed, null);
        }

        internal void Fail(Exception error)
        {
            Finish(PlayDataLoadStageEventKind.Failed, error);
        }

        public void Dispose()
        {
            Complete();
        }

        private void Finish(
            PlayDataLoadStageEventKind kind,
            Exception error)
        {
            if (Interlocked.Exchange(ref terminal, 1) != 0)
            {
                return;
            }

            stopwatch.Stop();
            Publish(kind, null, 0, 0, error);
        }

        private void Publish(
            PlayDataLoadStageEventKind kind,
            string activity,
            int completed,
            int total,
            Exception error)
        {
            events.Publish(Create(kind, activity, completed, total, error));
        }

        private PlayDataLoadStageEvent Create(
            PlayDataLoadStageEventKind kind,
            string activity,
            int completed,
            int total,
            Exception error)
        {
            return new PlayDataLoadStageEvent(
                Sequence,
                Stage,
                kind,
                stopwatch.Elapsed,
                activity,
                completed,
                total,
                error);
        }
    }
}
