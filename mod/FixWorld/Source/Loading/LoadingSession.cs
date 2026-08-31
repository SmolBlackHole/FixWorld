using System;
using System.Diagnostics;
using System.Threading;
using FixWorld.Scheduling;

namespace FixWorld.Loading
{
    internal static class LoadingSession
    {
        private static readonly object Sync = new object();
        private static readonly long DetailRefreshTicks =
            Math.Max(1L, Stopwatch.Frequency / 5L);

        private static volatile bool active;
        private static bool completed;
        private static long startedAt;
        private static LoadingStage currentStage;
        private static string currentStepName;
        private static string currentDetailName;
        private static string pendingDetailLabel;
        private static long nextDetailRefreshAt;
        private static double estimatedDurationMilliseconds;
        private static string currentActivity;
        private static LoadingStageEventSource currentSource;
        private static IDisposable stageSubscription;

        internal static bool IsActive => active;

        internal static void Start(bool readEstimate)
        {
            double previousDuration = readEstimate ? LoadingEstimateStore.Read() : 0.0;
            lock (Sync)
            {
                if (active || startedAt != 0L)
                {
                    return;
                }

                completed = false;
                startedAt = Stopwatch.GetTimestamp();
                currentStage = LoadingStage.Bootstrap;
                currentStepName = "FixWorld attached";
                currentDetailName = null;
                pendingDetailLabel = null;
                nextDetailRefreshAt = 0L;
                estimatedDurationMilliseconds = previousDuration;
                currentActivity = null;
                currentSource = LoadingStageEventSource.FixWorld;
                stageSubscription = LoadingEvents.Subscribe(ConsumeStageEvent);
                active = true;
            }
        }

        internal static bool TryComplete()
        {
            FixWorldScheduler.DrainEvents();
            double observedMilliseconds;
            IDisposable subscription;
            lock (Sync)
            {
                if (completed)
                {
                    return false;
                }

                completed = true;
                long completedAt = Stopwatch.GetTimestamp();
                active = false;
                currentStage = LoadingStage.Finalize;
                currentStepName = "Ready";
                currentActivity = null;
                ClearDetail();
                observedMilliseconds = ToMilliseconds(completedAt - startedAt);
                subscription = stageSubscription;
                stageSubscription = null;
            }

            subscription?.Dispose();
            LoadingEstimateStore.Write(observedMilliseconds);
            return true;
        }

        internal static bool TryGetSnapshot(out LoadingSnapshot snapshot)
        {
            FixWorldScheduler.DrainEvents();
            lock (Sync)
            {
                if (!active)
                {
                    snapshot = default;
                    return false;
                }

                long now = Stopwatch.GetTimestamp();
                RefreshDetail(now);
                double elapsedMilliseconds =
                    ToMilliseconds(Math.Max(0L, now - startedAt));
                bool hasEstimate = estimatedDurationMilliseconds > 0.0;
                float progress = hasEstimate
                    ? (float)Math.Min(
                        0.98,
                        elapsedMilliseconds / estimatedDurationMilliseconds)
                    : (float)Math.Min(0.95, ((int)currentStage - 0.5) / 5.0);
                snapshot = new LoadingSnapshot(
                    currentStage,
                    LoadingStageNames.GetName(currentStage),
                    currentDetailName ?? currentStepName,
                    elapsedMilliseconds,
                    Math.Max(0.02f, progress),
                    hasEstimate,
                    estimatedDurationMilliseconds,
                    currentActivity,
                    currentSource);
                return true;
            }
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void RefreshDetail(long now)
        {
            if (now < nextDetailRefreshAt)
            {
                return;
            }

            nextDetailRefreshAt = now + DetailRefreshTicks;
            string label = Interlocked.Exchange(ref pendingDetailLabel, null);
            if (label != null)
            {
                currentDetailName = LoaderStepCatalog.GetDisplayName(label);
            }
        }

        private static void ClearDetail()
        {
            currentDetailName = null;
            Interlocked.Exchange(ref pendingDetailLabel, null);
            nextDetailRefreshAt = 0L;
        }

        private static void ConsumeStageEvent(LoadingStageEvent stageEvent)
        {
            if (!active)
            {
                return;
            }

            if (stageEvent.Kind == LoadingStageEventKind.Detail)
            {
                Interlocked.Exchange(ref pendingDetailLabel, stageEvent.DisplayName);
                return;
            }

            if (stageEvent.Kind != LoadingStageEventKind.Started &&
                stageEvent.Kind != LoadingStageEventKind.Progress)
            {
                return;
            }

            lock (Sync)
            {
                if (!active)
                {
                    return;
                }

                ClearDetail();
                currentStage = stageEvent.Stage;
                currentStepName = stageEvent.DisplayName;
                currentActivity = stageEvent.Activity;
                currentSource = stageEvent.Source;
            }
        }
    }
}
