using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FixWorld.Runtime;
using Verse;

namespace FixWorld.Loading
{
    internal static class LoadingTelemetry
    {
        private const int TelemetrySampleRate = 128;
        private const int ProfilerLabelCacheLimit = 4096;
        private static readonly object Sync = new();
        private static readonly Dictionary<LoadingStep, StepStats> StepStatistics = [];
        private static readonly Dictionary<string, DelayedActionStats> DelayedActions =
            new(StringComparer.Ordinal);
        private static readonly Dictionary<string, StaticConstructorStats>
            StaticConstructors =
                new(StringComparer.Ordinal);
        private static readonly Dictionary<string, ModLoadingStats> ModStatistics =
            new(StringComparer.Ordinal);
        private static readonly long[] OverheadCalls = new long[3];
        private static readonly long[] OverheadTotalTicks = new long[3];
        private static readonly long[] OverheadMaxTicks = new long[3];

        private static volatile bool active;
        private static bool measureOverhead;
        private static long startedAt;
        private static long completedAt;
        private static long staticConstructorTailTicks;
        private static IDisposable stageEventSubscription;

        [ThreadStatic]
        private static List<ProfilerScope> profilerScopes;

        [ThreadStatic]
        private static Dictionary<string, DescriptorMatch> profilerLabelCache;

        [ThreadStatic]
        private static int telemetrySampleCounter;

        internal static void Start(bool collectOverhead)
        {
            lock (Sync)
            {
                if (active || startedAt != 0L)
                {
                    return;
                }

                StepStatistics.Clear();
                DelayedActions.Clear();
                StaticConstructors.Clear();
                ModStatistics.Clear();
                Array.Clear(OverheadCalls, 0, OverheadCalls.Length);
                Array.Clear(OverheadTotalTicks, 0, OverheadTotalTicks.Length);
                Array.Clear(OverheadMaxTicks, 0, OverheadMaxTicks.Length);
                measureOverhead = collectOverhead;
                startedAt = Stopwatch.GetTimestamp();
                completedAt = 0L;
                staticConstructorTailTicks = 0L;
                stageEventSubscription =
                    FixWorldEvents.Subscribe<LoadingStageEvent>(ConsumeStageEvent);
                active = true;
            }
        }

        internal static void Complete()
        {
            IDisposable subscription;
            lock (Sync)
            {
                if (!active)
                {
                    return;
                }

                completedAt = Stopwatch.GetTimestamp();
                active = false;
                subscription = stageEventSubscription;
                stageEventSubscription = null;
            }

            subscription?.Dispose();
        }

        internal static void BeginProfiler(string label)
        {
            if (!active)
            {
                return;
            }

            long overheadStartedAt = BeginTelemetrySample();
            try
            {
                if (profilerScopes == null)
                {
                    profilerScopes = new List<ProfilerScope>(16);
                }

                DescriptorMatch match = MatchProfilerLabel(label);
                bool recognized = match.Recognized;
                StepDescriptor descriptor = match.Descriptor;
                bool suppressed = recognized &&
                                  LoadingEvents.IsOperationActive(descriptor.Step);
                if (suppressed)
                {
                    recognized = false;
                }

                bool mainThread = UnityData.IsInMainThread;
                profilerScopes.Add(new ProfilerScope(
                    Stopwatch.GetTimestamp(),
                    recognized,
                    descriptor,
                    mainThread));

                if (recognized)
                {
                    LoadingEvents.ReportProfilerStep(descriptor, mainThread);
                }
                else if (!suppressed && mainThread)
                {
                    LoadingEvents.ReportProfilerDetail(label, mainThread);
                }
            }
            finally
            {
                ObserveMeasuredOverhead(overheadStartedAt);
            }
        }

        internal static void EndProfiler()
        {
            if (!active || profilerScopes == null || profilerScopes.Count == 0)
            {
                return;
            }

            long overheadStartedAt = BeginTelemetrySample();
            try
            {
                int scopeIndex = profilerScopes.Count - 1;
                ProfilerScope scope = profilerScopes[scopeIndex];
                profilerScopes.RemoveAt(scopeIndex);
                long elapsedTicks = Stopwatch.GetTimestamp() - scope.StartedAt;
                long exclusiveTicks = Math.Max(0L, elapsedTicks - scope.ChildTicks);

                if (profilerScopes.Count > 0)
                {
                    int parentIndex = profilerScopes.Count - 1;
                    ProfilerScope parent = profilerScopes[parentIndex];
                    parent.ChildTicks += elapsedTicks;
                    profilerScopes[parentIndex] = parent;
                }

                if (!scope.Recognized)
                {
                    return;
                }

                lock (Sync)
                {
                    if (!StepStatistics.TryGetValue(
                            scope.Descriptor.Step,
                            out StepStats stats))
                    {
                        stats = new StepStats(scope.Descriptor);
                        StepStatistics.Add(scope.Descriptor.Step, stats);
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
                }

                bool hasParent = TryFindParentDescriptor(out StepDescriptor parentDescriptor);
                if (hasParent)
                {
                    LoadingEvents.ReportProfilerStep(parentDescriptor, scope.MainThread);
                }
                else
                {
                    LoadingEvents.ReportStageFallback(
                        scope.Descriptor.Stage,
                        scope.MainThread);
                }
            }
            finally
            {
                ObserveMeasuredOverhead(overheadStartedAt);
            }
        }

        internal static void ObserveWork(
            LoadingWorkItem item,
            long executionTicks,
            long mainThreadTicks,
            long workerThreadTicks,
            long waitTicks,
            long wallTicks,
            bool succeeded)
        {
            if (!active)
            {
                return;
            }

            long overheadStartedAt = BeginTelemetrySample();
            try
            {
                lock (Sync)
                {
                    string modKey = item.Attribution.PackageId + "\n" +
                                    item.Attribution.Quality + "\n" +
                                    item.Stage + "\n" + item.Operation;
                    if (!ModStatistics.TryGetValue(modKey, out ModLoadingStats modStats))
                    {
                        modStats = new ModLoadingStats(item);
                        ModStatistics.Add(modKey, modStats);
                    }

                    modStats.Calls++;
                    modStats.ExecutionTicks += executionTicks;
                    modStats.MainThreadTicks += mainThreadTicks;
                    modStats.WorkerThreadTicks += workerThreadTicks;
                    modStats.WaitTicks += waitTicks;
                    modStats.WallTicks += wallTicks;
                    if (!succeeded)
                    {
                        modStats.Failures++;
                    }

                    if (item.Operation == LoadingStep.RunStaticConstructors)
                    {
                        string constructorKey = item.Attribution.PackageId + "\n" +
                                                item.Subject;
                        if (!StaticConstructors.TryGetValue(
                                constructorKey,
                                out StaticConstructorStats constructorStats))
                        {
                            constructorStats = new StaticConstructorStats(item);
                            StaticConstructors.Add(constructorKey, constructorStats);
                        }

                        constructorStats.Calls++;
                        constructorStats.TotalTicks += executionTicks;
                        constructorStats.MaxTicks = Math.Max(
                            constructorStats.MaxTicks,
                            executionTicks);
                        if (!succeeded)
                        {
                            constructorStats.Failures++;
                        }
                    }

                    if (item.Operation == LoadingStep.FinalizeStaticInitialization)
                    {
                        staticConstructorTailTicks += executionTicks;
                    }
                }
            }
            finally
            {
                ObserveMeasuredOverhead(overheadStartedAt);
            }
        }

        internal static void ObserveDelayedAction(
            LoadingActionPlan plan,
            long executionTicks)
        {
            if (!active)
            {
                return;
            }

            long overheadStartedAt = BeginTelemetrySample();
            try
            {
                string key = plan.Attribution.PackageId + "\n" + plan.Label;
                lock (Sync)
                {
                    if (!DelayedActions.TryGetValue(key, out DelayedActionStats stats))
                    {
                        stats = new DelayedActionStats(plan);
                        DelayedActions.Add(key, stats);
                    }

                    stats.Calls++;
                    stats.TotalTicks += executionTicks;
                    stats.MaxTicks = Math.Max(stats.MaxTicks, executionTicks);
                }
            }
            finally
            {
                ObserveMeasuredOverhead(overheadStartedAt);
            }
        }

        internal static void ObserveOverhead(LoadingOverheadKind kind, long elapsedTicks)
        {
            if (!measureOverhead || elapsedTicks <= 0L)
            {
                return;
            }

            int index = (int)kind;
            Interlocked.Increment(ref OverheadCalls[index]);
            Interlocked.Add(ref OverheadTotalTicks[index], elapsedTicks);
            UpdateMaximum(ref OverheadMaxTicks[index], elapsedTicks);
        }

        internal static LoadingMeasurement GetMeasurement()
        {
            lock (Sync)
            {
                long end = completedAt != 0L ? completedAt : Stopwatch.GetTimestamp();
                List<LoadingStepMeasurement> steps = StepStatistics.Values
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
                List<DelayedActionSnapshot> delayedActions = DelayedActions.Values
                    .OrderByDescending(item => item.TotalTicks)
                    .Select(item => new DelayedActionSnapshot(
                        item.Label,
                        item.Attribution.PackageId,
                        item.Attribution.ModName,
                        item.Calls,
                        ToMilliseconds(item.TotalTicks),
                        ToMilliseconds(item.MaxTicks)))
                    .ToList();
                List<StaticConstructorSnapshot> staticConstructors =
                    StaticConstructors.Values
                        .OrderByDescending(item => item.TotalTicks)
                        .Select(item => new StaticConstructorSnapshot(
                            item.TypeName,
                            item.Attribution.PackageId,
                            item.Attribution.ModName,
                            item.Calls,
                            ToMilliseconds(item.TotalTicks),
                            ToMilliseconds(item.MaxTicks),
                            item.Failures))
                        .ToList();
                List<ModLoadingMeasurement> mods = ModStatistics.Values
                    .OrderByDescending(item => item.WallTicks)
                    .ThenBy(item => item.Attribution.PackageId, StringComparer.Ordinal)
                    .ThenBy(item => item.Operation)
                    .Select(item => new ModLoadingMeasurement(
                        item.Attribution.PackageId,
                        item.Attribution.ModName,
                        item.Attribution.Quality,
                        item.Stage,
                        item.Operation,
                        item.Calls,
                        item.Failures,
                        ToMilliseconds(item.ExecutionTicks),
                        ToMilliseconds(item.MainThreadTicks),
                        ToMilliseconds(item.WorkerThreadTicks),
                        ToMilliseconds(item.WaitTicks),
                        ToMilliseconds(item.WallTicks)))
                    .ToList();
                List<LoadingOverheadMeasurement> overhead = Enum
                    .GetValues(typeof(LoadingOverheadKind))
                    .Cast<LoadingOverheadKind>()
                    .Select(kind => new
                    {
                        Kind = kind,
                        Calls = Interlocked.Read(ref OverheadCalls[(int)kind]),
                        TotalTicks = Interlocked.Read(
                            ref OverheadTotalTicks[(int)kind]),
                        MaxTicks = Interlocked.Read(ref OverheadMaxTicks[(int)kind])
                    })
                    .Where(item => item.Calls > 0L)
                    .Select(item => new LoadingOverheadMeasurement(
                        item.Kind,
                        item.Calls,
                        ToMilliseconds(item.TotalTicks),
                        ToMilliseconds(item.MaxTicks),
                        item.Kind == LoadingOverheadKind.Telemetry))
                    .ToList();
                return new LoadingMeasurement(
                    ToMilliseconds(Math.Max(0L, end - startedAt)),
                    steps,
                    delayedActions,
                    staticConstructors,
                    ToMilliseconds(staticConstructorTailTicks),
                    mods,
                    overhead);
            }
        }

        internal static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static bool TryFindParentDescriptor(out StepDescriptor descriptor)
        {
            for (int index = profilerScopes.Count - 1; index >= 0; index--)
            {
                if (profilerScopes[index].Recognized)
                {
                    descriptor = profilerScopes[index].Descriptor;
                    return true;
                }
            }

            descriptor = default;
            return false;
        }

        private static void ObserveMeasuredOverhead(long started)
        {
            if (started != 0L)
            {
                ObserveSampledTelemetryOverhead(Stopwatch.GetTimestamp() - started);
            }
        }

        private static long BeginTelemetrySample()
        {
            if (!measureOverhead)
            {
                return 0L;
            }

            telemetrySampleCounter++;
            return telemetrySampleCounter % TelemetrySampleRate == 0
                ? Stopwatch.GetTimestamp()
                : 0L;
        }

        private static void ObserveSampledTelemetryOverhead(long elapsedTicks)
        {
            int index = (int)LoadingOverheadKind.Telemetry;
            Interlocked.Add(ref OverheadCalls[index], TelemetrySampleRate);
            Interlocked.Add(
                ref OverheadTotalTicks[index],
                elapsedTicks * TelemetrySampleRate);
            UpdateMaximum(ref OverheadMaxTicks[index], elapsedTicks);
        }

        private static DescriptorMatch MatchProfilerLabel(string label)
        {
            if (label == null)
            {
                return new DescriptorMatch(false, default);
            }

            if (profilerLabelCache == null)
            {
                profilerLabelCache =
                    new Dictionary<string, DescriptorMatch>(StringComparer.Ordinal);
            }

            if (profilerLabelCache.TryGetValue(label, out DescriptorMatch match))
            {
                return match;
            }

            bool recognized =
                LoaderStepCatalog.TryMatch(label, out StepDescriptor descriptor);
            match = new DescriptorMatch(recognized, descriptor);
            if (profilerLabelCache.Count >= ProfilerLabelCacheLimit)
            {
                profilerLabelCache.Clear();
            }

            profilerLabelCache.Add(label, match);
            return match;
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long current = Interlocked.Read(ref target);
            while (value > current)
            {
                long observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }

        private static void ConsumeStageEvent(LoadingStageEvent stageEvent)
        {
            if (!active || stageEvent.OperationId == 0L ||
                (stageEvent.Kind != LoadingStageEventKind.Completed &&
                 stageEvent.Kind != LoadingStageEventKind.Failed))
            {
                return;
            }

            long elapsedTicks = Math.Max(0L, stageEvent.ElapsedTicks);
            lock (Sync)
            {
                if (!active)
                {
                    return;
                }

                if (!StepStatistics.TryGetValue(stageEvent.Operation, out StepStats stepStats))
                {
                    stepStats = new StepStats(new StepDescriptor(
                        stageEvent.Operation,
                        stageEvent.Stage,
                        stageEvent.DisplayName));
                    StepStatistics.Add(stageEvent.Operation, stepStats);
                }

                stepStats.Calls++;
                stepStats.TotalTicks += elapsedTicks;
                stepStats.ExclusiveTicks += elapsedTicks;
                if (stageEvent.MainThread)
                {
                    stepStats.MainThreadTicks += elapsedTicks;
                    stepStats.MainThreadExclusiveTicks += elapsedTicks;
                }
                else
                {
                    stepStats.WorkerThreadTicks += elapsedTicks;
                    stepStats.WorkerThreadExclusiveTicks += elapsedTicks;
                }

                if (!stageEvent.RecordModTime)
                {
                    return;
                }

                string modKey = stageEvent.Attribution.PackageId + "\n" +
                                stageEvent.Attribution.Quality + "\n" +
                                stageEvent.Stage + "\n" + stageEvent.Operation;
                if (!ModStatistics.TryGetValue(modKey, out ModLoadingStats modStats))
                {
                    modStats = new ModLoadingStats(stageEvent);
                    ModStatistics.Add(modKey, modStats);
                }

                modStats.Calls++;
                modStats.ExecutionTicks += elapsedTicks;
                modStats.WallTicks += elapsedTicks;
                if (stageEvent.MainThread)
                {
                    modStats.MainThreadTicks += elapsedTicks;
                }
                else
                {
                    modStats.WorkerThreadTicks += elapsedTicks;
                }

                if (stageEvent.Kind == LoadingStageEventKind.Failed)
                {
                    modStats.Failures++;
                }
            }
        }

        private struct ProfilerScope
        {
            internal readonly long StartedAt;
            internal readonly bool Recognized;
            internal readonly StepDescriptor Descriptor;
            internal readonly bool MainThread;
            internal long ChildTicks;

            internal ProfilerScope(
                long startedAt,
                bool recognized,
                StepDescriptor descriptor,
                bool mainThread)
            {
                StartedAt = startedAt;
                Recognized = recognized;
                Descriptor = descriptor;
                MainThread = mainThread;
                ChildTicks = 0L;
            }
        }

        private readonly struct DescriptorMatch
        {
            internal readonly bool Recognized;
            internal readonly StepDescriptor Descriptor;

            internal DescriptorMatch(bool recognized, StepDescriptor descriptor)
            {
                Recognized = recognized;
                Descriptor = descriptor;
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

        private sealed class DelayedActionStats
        {
            internal readonly string Label;
            internal readonly LoadingModAttribution Attribution;
            internal long Calls;
            internal long TotalTicks;
            internal long MaxTicks;

            internal DelayedActionStats(LoadingActionPlan plan)
            {
                Label = plan.Label;
                Attribution = plan.Attribution;
            }
        }

        private sealed class StaticConstructorStats
        {
            internal readonly string TypeName;
            internal readonly LoadingModAttribution Attribution;
            internal long Calls;
            internal long TotalTicks;
            internal long MaxTicks;
            internal long Failures;

            internal StaticConstructorStats(LoadingWorkItem item)
            {
                TypeName = item.Subject;
                Attribution = item.Attribution;
            }
        }

        private sealed class ModLoadingStats
        {
            internal readonly LoadingModAttribution Attribution;
            internal readonly LoadingStage Stage;
            internal readonly LoadingStep Operation;
            internal long Calls;
            internal long Failures;
            internal long ExecutionTicks;
            internal long MainThreadTicks;
            internal long WorkerThreadTicks;
            internal long WaitTicks;
            internal long WallTicks;

            internal ModLoadingStats(LoadingWorkItem item)
            {
                Attribution = item.Attribution;
                Stage = item.Stage;
                Operation = item.Operation;
            }

            internal ModLoadingStats(LoadingStageEvent stageEvent)
            {
                Attribution = stageEvent.Attribution;
                Stage = stageEvent.Stage;
                Operation = stageEvent.Operation;
            }
        }

    }
}
