using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

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
                active = true;
            }
        }

        internal static bool TryComplete()
        {
            double observedMilliseconds;
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
            }

            LoadingEstimateStore.Write(observedMilliseconds);
            return true;
        }

        internal static void ReportProfilerStep(StepDescriptor descriptor)
        {
            if (!active)
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
                currentStage = descriptor.Stage;
                currentStepName = descriptor.DisplayName;
                currentActivity = descriptor.ModName == null
                    ? null
                    : descriptor.ModActivity + " for " + descriptor.ModName;
            }
        }

        internal static void ReportProfilerDetail(string label)
        {
            if (active)
            {
                Interlocked.Exchange(ref pendingDetailLabel, label);
            }
        }

        internal static void RestoreProfilerStep(
            bool hasParent,
            StepDescriptor parent,
            LoadingStage completedStage)
        {
            if (!active)
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
                if (hasParent)
                {
                    currentStage = parent.Stage;
                    currentStepName = parent.DisplayName;
                    currentActivity = parent.ModName == null
                        ? null
                        : parent.ModActivity + " for " + parent.ModName;
                    return;
                }

                currentStage = completedStage;
                currentStepName = GetStageFallbackName(completedStage);
                currentActivity = null;
            }
        }

        internal static void ReportStage(
            LoadingPipelineStage stage,
            int completedTasks,
            int totalTasks)
        {
            if (!active)
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
                currentStage = stage.Phase;
                currentStepName = stage.Name;
                currentActivity = string.Format(
                    CultureInfo.InvariantCulture,
                    "Stage tasks {0:N0} / {1:N0}   {2}",
                    completedTasks,
                    totalTasks,
                    stage.ExecutionMode);
            }
        }

        internal static void ReportWork(
            LoadingWorkItem item,
            int currentAction,
            int totalActions)
        {
            if (!active)
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
                currentStage = item.Stage;
                currentStepName = item.DisplayName;
                currentActivity = item.Activity ?? string.Format(
                    CultureInfo.InvariantCulture,
                    "Delayed initialization task {0:N0} / {1:N0}",
                    currentAction,
                    totalActions);
            }
        }

        internal static bool TryGetSnapshot(out LoadingSnapshot snapshot)
        {
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
                    GetStageName(currentStage),
                    currentDetailName ?? currentStepName,
                    elapsedMilliseconds,
                    Math.Max(0.02f, progress),
                    hasEstimate,
                    estimatedDurationMilliseconds,
                    currentActivity);
                return true;
            }
        }

        internal static string GetStageName(LoadingStage stage)
        {
            switch (stage)
            {
                case LoadingStage.Bootstrap: return "Bootstrap";
                case LoadingStage.XmlAndPatches: return "XML & patches";
                case LoadingStage.Definitions: return "Definitions";
                case LoadingStage.Content: return "Content";
                case LoadingStage.Finalize: return "Finalize";
                default: throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        private static string GetStageFallbackName(LoadingStage stage)
        {
            switch (stage)
            {
                case LoadingStage.Bootstrap: return "Preparing the mod environment";
                case LoadingStage.XmlAndPatches: return "Processing XML and patches";
                case LoadingStage.Definitions: return "Preparing game definitions";
                case LoadingStage.Content: return "Preparing mod content";
                case LoadingStage.Finalize: return "Finalizing startup";
                default: throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
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
    }
}
