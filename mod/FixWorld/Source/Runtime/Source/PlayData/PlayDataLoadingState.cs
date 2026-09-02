using System;
using System.Diagnostics;
using FixWorld.Events;
using FixWorld.Loading;

namespace FixWorld.PlayData
{
    internal sealed class PlayDataLoadingState : IDisposable
    {
        private readonly object sync = new object();
        private readonly IDisposable subscription;

        private bool active;
        private long startedAt;
        private PlayDataLoadStage stage;
        private string activity;
        private double estimatedDurationMilliseconds;

        internal PlayDataLoadingState(EventBus events)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            subscription = events.Subscribe<PlayDataLoadStageEvent>(Observe);
        }

        internal void Start()
        {
            lock (sync)
            {
                if (active)
                {
                    throw new InvalidOperationException(
                        "A play-data loading session is already active.");
                }

                active = true;
                startedAt = Stopwatch.GetTimestamp();
                stage = PlayDataLoadStage.Reset;
                activity = null;
                estimatedDurationMilliseconds = LoadingEstimateStore.Read();
            }
        }

        internal bool Complete()
        {
            double observed;
            lock (sync)
            {
                if (!active)
                {
                    return false;
                }

                active = false;
                stage = PlayDataLoadStage.Complete;
                activity = null;
                observed = ToMilliseconds(
                    Stopwatch.GetTimestamp() - startedAt);
            }

            LoadingEstimateStore.Write(observed);
            return true;
        }

        internal void Abort()
        {
            lock (sync)
            {
                active = false;
                activity = null;
            }
        }

        internal bool TryGetSnapshot(out PlayDataLoadingSnapshot snapshot)
        {
            lock (sync)
            {
                if (!active)
                {
                    snapshot = default;
                    return false;
                }

                double elapsed = ToMilliseconds(
                    Stopwatch.GetTimestamp() - startedAt);
                bool hasEstimate = estimatedDurationMilliseconds > 0.0;
                float stageProgress = Math.Max(
                    0.02f,
                    Math.Min(
                        0.98f,
                        ((int)stage - 0.5f) /
                        PlayDataLoadStageCatalog.Count));
                float progress = hasEstimate
                    ? Math.Max(
                        stageProgress,
                        (float)Math.Min(
                            0.98,
                            elapsed / estimatedDurationMilliseconds))
                    : stageProgress;
                snapshot = new PlayDataLoadingSnapshot(
                    stage,
                    elapsed,
                    progress,
                    hasEstimate,
                    estimatedDurationMilliseconds,
                    activity);
                return true;
            }
        }

        public void Dispose()
        {
            subscription.Dispose();
        }

        private void Observe(PlayDataLoadStageEvent stageEvent)
        {
            lock (sync)
            {
                if (!active)
                {
                    return;
                }

                if (stageEvent.Kind == PlayDataLoadStageEventKind.Started ||
                    stageEvent.Kind == PlayDataLoadStageEventKind.Progress ||
                    stageEvent.Kind == PlayDataLoadStageEventKind.Failed)
                {
                    stage = stageEvent.Stage;
                    activity = stageEvent.Activity;
                }
            }
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }

    internal readonly struct PlayDataLoadingSnapshot
    {
        internal PlayDataLoadingSnapshot(
            PlayDataLoadStage stage,
            double elapsedMilliseconds,
            float progress,
            bool hasDurationEstimate,
            double estimatedTotalMilliseconds,
            string activity)
        {
            Stage = stage;
            ElapsedMilliseconds = elapsedMilliseconds;
            Progress = progress;
            HasDurationEstimate = hasDurationEstimate;
            EstimatedTotalMilliseconds = estimatedTotalMilliseconds;
            Activity = activity;
        }

        internal PlayDataLoadStage Stage { get; }

        internal double ElapsedMilliseconds { get; }

        internal float Progress { get; }

        internal bool HasDurationEstimate { get; }

        internal double EstimatedTotalMilliseconds { get; }

        internal string Activity { get; }
    }
}
