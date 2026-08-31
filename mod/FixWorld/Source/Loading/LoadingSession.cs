using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Verse;

namespace FixWorld.Loading
{
    internal static class LoadingSession
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<LoadingStep, StepStats> Stats =
            new Dictionary<LoadingStep, StepStats>();
        private static readonly long DetailRefreshTicks =
            Math.Max(1L, Stopwatch.Frequency / 5L);

        private static volatile bool active;
        private static bool completed;
        private static long startedAt;
        private static long completedAt;
        private static long sequence;
        private static long currentSequence;
        private static LoadingStage currentStage;
        private static string currentStepName;
        private static string currentDetailName;
        private static string pendingDetailLabel;
        private static long nextDetailRefreshAt;
        private static double estimatedDurationMilliseconds;
        private static long currentModSequence;
        private static string currentActivity;

        [ThreadStatic]
        private static Stack<Scope> scopes;

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

                Stats.Clear();
                completed = false;
                startedAt = Stopwatch.GetTimestamp();
                completedAt = 0L;
                currentStage = LoadingStage.Bootstrap;
                currentStepName = "FixWorld attached";
                currentDetailName = null;
                pendingDetailLabel = null;
                nextDetailRefreshAt = 0L;
                currentSequence = 0L;
                sequence = 0L;
                estimatedDurationMilliseconds = previousDuration;
                ClearCurrentMod();
                active = true;
            }
        }

        internal static void Begin(string label)
        {
            if (!active)
            {
                return;
            }

            if (scopes == null)
            {
                scopes = new Stack<Scope>();
            }

            bool recognized = LoaderStepCatalog.TryMatch(label, out StepDescriptor descriptor);
            long scopeSequence = recognized ? Interlocked.Increment(ref sequence) : 0L;
            Scope scope = new Scope(
                Stopwatch.GetTimestamp(),
                recognized,
                descriptor,
                scopeSequence,
                UnityData.IsInMainThread);
            scopes.Push(scope);

            if (!recognized)
            {
                Interlocked.Exchange(ref pendingDetailLabel, label);
                return;
            }

            lock (Sync)
            {
                if (!active)
                {
                    return;
                }

                ClearDetail();
                currentSequence = scopeSequence;
                currentStage = descriptor.Stage;
                currentStepName = descriptor.DisplayName;
                if (descriptor.ModName != null)
                {
                    SetCurrentMod(scopeSequence, descriptor);
                }
            }
        }

        internal static void End()
        {
            if (!active || scopes == null || scopes.Count == 0)
            {
                return;
            }

            Scope scope = scopes.Pop();
            long elapsedTicks = Stopwatch.GetTimestamp() - scope.StartedAt;
            long exclusiveTicks = Math.Max(0L, elapsedTicks - scope.ChildTicks);
            if (scopes.Count > 0)
            {
                scopes.Peek().ChildTicks += elapsedTicks;
            }

            if (!scope.Recognized)
            {
                return;
            }

            lock (Sync)
            {
                if (!Stats.TryGetValue(scope.Descriptor.Step, out StepStats stats))
                {
                    stats = new StepStats(scope.Descriptor);
                    Stats.Add(scope.Descriptor.Step, stats);
                }

                stats.Calls++;
                stats.TotalTicks += elapsedTicks;
                stats.ExclusiveTicks += exclusiveTicks;
                if (scope.MainThread)
                {
                    stats.MainThreadTicks += elapsedTicks;
                    stats.MainThreadExclusiveTicks += exclusiveTicks;
                }
                else
                {
                    stats.WorkerThreadTicks += elapsedTicks;
                    stats.WorkerThreadExclusiveTicks += exclusiveTicks;
                }

                if (currentSequence != scope.Sequence)
                {
                    RestoreCurrentModAfter(scope);
                    return;
                }

                ClearDetail();
                Scope parent = scopes.FirstOrDefault(candidate => candidate.Recognized);
                if (parent != null)
                {
                    currentSequence = parent.Sequence;
                    currentStage = parent.Descriptor.Stage;
                    currentStepName = parent.Descriptor.DisplayName;
                }
                else
                {
                    currentSequence = 0L;
                    currentStepName = GetStageFallbackName(scope.Descriptor.Stage);
                }

                RestoreCurrentModAfter(scope);
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
                completedAt = Stopwatch.GetTimestamp();
                active = false;
                currentSequence = 0L;
                currentStage = LoadingStage.Finalize;
                currentStepName = "Ready";
                ClearDetail();
                observedMilliseconds = ToMilliseconds(completedAt - startedAt);
            }

            LoadingEstimateStore.Write(observedMilliseconds);
            return true;
        }

        internal static void ReportDelayedInitialization(
            string label,
            int currentTask,
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
                currentStage = LoadingStage.Content;
                currentStepName = LoaderStepCatalog.GetDisplayName(label);
                currentActivity = string.Format(
                    CultureInfo.InvariantCulture,
                    "Delayed initialization task {0:N0} / {1:N0}",
                    currentTask,
                    totalTasks);
            }
        }

        internal static void ReportContentLoading(
            string modName,
            string activity,
            int currentStep,
            int totalSteps)
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
                currentStage = LoadingStage.Content;
                currentStepName = "Loading content for " + modName;
                currentActivity = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}   {1} / {2}",
                    activity,
                    currentStep,
                    totalSteps);
            }
        }

        internal static void ReportStaticConstructor(
            string typeName,
            string modName,
            int current,
            int total)
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
                currentStage = LoadingStage.Finalize;
                currentStepName = "Initializing " + modName;
                currentActivity = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}   {1:N0} / {2:N0}",
                    typeName,
                    current,
                    total);
            }
        }

        internal static void ReportFinalization(string stepName, string activity)
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
                currentStage = LoadingStage.Finalize;
                currentStepName = stepName;
                currentActivity = activity;
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
                    ? (float)Math.Min(0.98, elapsedMilliseconds / estimatedDurationMilliseconds)
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

        internal static LoadingMeasurement GetMeasurement()
        {
            lock (Sync)
            {
                long end = completedAt != 0L ? completedAt : Stopwatch.GetTimestamp();
                List<LoadingStepMeasurement> steps = Stats.Values
                    .OrderBy(item => item.Descriptor.Stage)
                    .ThenBy(item => item.Descriptor.Step)
                    .Select(item => new LoadingStepMeasurement(
                        item.Descriptor.Step,
                        item.Descriptor.Stage,
                        item.Descriptor.Name,
                        item.Calls,
                        ToMilliseconds(item.TotalTicks),
                        ToMilliseconds(item.ExclusiveTicks),
                        ToMilliseconds(item.MainThreadTicks),
                        ToMilliseconds(item.WorkerThreadTicks),
                        ToMilliseconds(item.MainThreadExclusiveTicks),
                        ToMilliseconds(item.WorkerThreadExclusiveTicks)))
                    .ToList();
                return new LoadingMeasurement(
                    ToMilliseconds(Math.Max(0L, end - startedAt)),
                    steps);
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

        private static void RestoreCurrentModAfter(Scope completedScope)
        {
            if (currentModSequence != completedScope.Sequence)
            {
                return;
            }

            Scope parent = scopes.FirstOrDefault(candidate =>
                candidate.Recognized && candidate.Descriptor.ModName != null);
            if (parent == null)
            {
                ClearCurrentMod();
                return;
            }

            SetCurrentMod(parent.Sequence, parent.Descriptor);
        }

        private static void SetCurrentMod(long scopeSequence, StepDescriptor descriptor)
        {
            currentModSequence = scopeSequence;
            currentActivity = descriptor.ModActivity + " for " + descriptor.ModName;
        }

        private static void ClearCurrentMod()
        {
            currentModSequence = 0L;
            currentActivity = null;
        }

        private sealed class Scope
        {
            internal readonly long StartedAt;
            internal readonly bool Recognized;
            internal readonly StepDescriptor Descriptor;
            internal readonly long Sequence;
            internal readonly bool MainThread;
            internal long ChildTicks;

            internal Scope(
                long startedAt,
                bool recognized,
                StepDescriptor descriptor,
                long sequence,
                bool mainThread)
            {
                StartedAt = startedAt;
                Recognized = recognized;
                Descriptor = descriptor;
                Sequence = sequence;
                MainThread = mainThread;
            }
        }

        private sealed class StepStats
        {
            internal readonly StepDescriptor Descriptor;
            internal long Calls;
            internal long TotalTicks;
            internal long ExclusiveTicks;
            internal long MainThreadTicks;
            internal long WorkerThreadTicks;
            internal long MainThreadExclusiveTicks;
            internal long WorkerThreadExclusiveTicks;

            internal StepStats(StepDescriptor descriptor)
            {
                Descriptor = descriptor;
            }
        }

    }
}
