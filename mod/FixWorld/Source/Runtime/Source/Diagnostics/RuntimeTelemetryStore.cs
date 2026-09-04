using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;
using FixWorld.Loading;
using FixWorld.PlayData;
using FixWorld.Preloader;
using FixWorld.Profiling;
using FixWorld.Textures;

namespace FixWorld.Diagnostics
{
    internal sealed class RuntimeTelemetryStore : IDisposable
    {
        private const int TargetHistoryCapacity = 1024;
        private const int TargetHistoryMask = TargetHistoryCapacity - 1;
        private const int TargetReuseWindowTicks = 600;

        private readonly object sync = new();
        private readonly Profiler<RuntimeHotpath> runtimeProfiler;
        private readonly ProfileSlot<RuntimeHotpath>[] runtimeSlots;
        private readonly long[] pathConstraintCounts =
            new long[PathRequestCatalog.ConstraintCount];
        private readonly long[] pathDistanceBuckets =
            new long[PathRequestCatalog.DistanceBucketCount];
        private readonly long[] pathEndModes =
            new long[PathRequestCatalog.EndModeCount];
        private readonly long[] pathPawnCategories =
            new long[PathRequestCatalog.PawnCategoryCount];
        private readonly long[] pathTargetHistoryKeys =
            new long[TargetHistoryCapacity];
        private readonly int[] pathTargetHistoryTicks =
            new int[TargetHistoryCapacity];
        private readonly long[] pathTargetKinds =
            new long[PathRequestCatalog.TargetKindCount];
        private readonly long[] pathTraversalModes =
            new long[PathRequestCatalog.TraversalModeCount];

        private bool active;
        private long pathBatches;
        private long pathDataUpdates;
        private long pathDirtyCells;
        private long pathExpandedCellVisits;
        private long pathGridJobsCreated;
        private long pathChunks8;
        private long pathChunks16;
        private long pathChunks32;
        private long pathMaximumChunks8;
        private long pathMaximumChunks16;
        private long pathMaximumChunks32;
        private long pathMaximumBatchSize;
        private long pathMaximumDistance;
        private long pathMaximumDirtyCells;
        private long pathMaximumUniqueExpandedCells;
        private long pathMaximumQueueDelayTicks;
        private long pathRequestObservations;
        private long pathRepeatedTargets;
        private long pathRequests;
        private long pathTargetTrackerCollisions;
        private long pathTotalDistance;
        private long pathTotalQueueDelayTicks;
        private long pathUniqueExpandedCells;
        private long reachabilityCacheHits;
        private long reachabilityCacheMisses;
        private double estimatedDurationMilliseconds;
        private LoadingLiveState liveState;
        private Profiler<PlayDataLoadStage> profiler;
        private ProfileSlot<PlayDataLoadStage>[] stageSlots;
        private PlayDataLoadStage stage;
        private long stageStartedAt;
        private long startedAt;
        private bool stagesComplete;
        private int disposed;

        internal RuntimeTelemetryStore()
        {
            runtimeProfiler = new(options: ProfilerOptions.Buffered);
            runtimeSlots = CreateRuntimeSlots(runtimeProfiler);
        }

        internal bool Start()
        {
            lock (sync)
            {
                if (active)
                {
                    return false;
                }

                active = true;
                stagesComplete = false;
                profiler = new(
                    options: ProfilerOptions.Inline);
                stageSlots = CreateStageSlots(profiler);
                startedAt = Stopwatch.GetTimestamp();
                estimatedDurationMilliseconds = LoadingEstimateStore.Read();
                StartStage(PlayDataLoadStage.Reset, startedAt);
                PublishLiveState();
                return true;
            }
        }

        internal bool Transition(PlayDataLoadStage next)
        {
            lock (sync)
            {
                if (!active || stagesComplete ||
                    (int)next != (int)stage + 1)
                {
                    return false;
                }

                long now = Stopwatch.GetTimestamp();
                FinishStage(now, succeeded: true);
                StartStage(next, now);
                PublishLiveState();
                return true;
            }
        }

        internal bool CompletePlayData()
        {
            lock (sync)
            {
                if (!active || stagesComplete)
                {
                    return false;
                }

                long now = Stopwatch.GetTimestamp();
                FinishStage(now, succeeded: true);
                StartStage(PlayDataLoadStage.Complete, now);
                FinishStage(Stopwatch.GetTimestamp(), succeeded: true);
                stagesComplete = true;
                PublishLiveState();
                return true;
            }
        }

        internal bool Abort()
        {
            Profiler<PlayDataLoadStage> abandonedProfiler;
            lock (sync)
            {
                if (!active)
                {
                    return false;
                }

                if (active && !stagesComplete)
                {
                    FinishStage(Stopwatch.GetTimestamp(), succeeded: false);
                }

                active = false;
                stagesComplete = false;
                stageSlots = null;
                abandonedProfiler = profiler;
                profiler = null;
                startedAt = 0L;
                stageStartedAt = 0L;
                Volatile.Write(ref liveState, null);
            }

            abandonedProfiler?.Dispose();
            return true;
        }

        internal bool TryGetLoadingSnapshot(
            out PlayDataLoadingSnapshot snapshot)
        {
            LoadingLiveState current = Volatile.Read(ref liveState);
            if (current == null)
            {
                snapshot = default;
                return false;
            }

            double elapsed = ToMilliseconds(
                Stopwatch.GetTimestamp() - current.StartedAt);
            bool hasEstimate = current.EstimatedDurationMilliseconds > 0.0;
            float stageProgress = Math.Max(
                0.02f,
                Math.Min(
                    0.98f,
                    ((int)current.Stage - 0.5f) /
                    PlayDataLoadStageCatalog.Count));
            float progress = hasEstimate
                ? Math.Max(
                    stageProgress,
                    (float)Math.Min(
                        0.98,
                        elapsed / current.EstimatedDurationMilliseconds))
                : stageProgress;
            snapshot = new PlayDataLoadingSnapshot(
                current.Stage,
                elapsed,
                progress,
                hasEstimate,
                current.EstimatedDurationMilliseconds);
            return true;
        }

        internal long StartRuntimeHotpath(RuntimeHotpath hotpath) =>
            runtimeSlots[(int)hotpath].StartTimestamp();

        internal void StopRuntimeHotpath(
            RuntimeHotpath hotpath,
            long startedAt) =>
            runtimeSlots[(int)hotpath].StopTimestamp(startedAt);

        internal void ObservePathBatch(
            int requests,
            long totalQueueDelayTicks,
            int maximumQueueDelayTicks)
        {
            Interlocked.Increment(ref pathBatches);
            Interlocked.Add(ref pathRequests, requests);
            Interlocked.Add(
                ref pathTotalQueueDelayTicks,
                totalQueueDelayTicks);
            UpdateMaximum(ref pathMaximumBatchSize, requests);
            UpdateMaximum(
                ref pathMaximumQueueDelayTicks,
                maximumQueueDelayTicks);
        }

        internal void ObservePathGridJobCreated() =>
            Interlocked.Increment(ref pathGridJobsCreated);

        internal void ObservePathRequest(in PathRequestObservation observation)
        {
            Interlocked.Increment(ref pathRequestObservations);
            Interlocked.Increment(
                ref pathPawnCategories[(int)observation.PawnCategory]);
            Interlocked.Increment(
                ref pathTraversalModes[(int)observation.TraversalMode]);
            Interlocked.Increment(
                ref pathEndModes[(int)observation.EndMode]);
            Interlocked.Increment(
                ref pathTargetKinds[(int)observation.TargetKind]);

            int distance = Math.Max(0, observation.Distance);
            Interlocked.Add(ref pathTotalDistance, distance);
            UpdateMaximum(ref pathMaximumDistance, distance);
            Interlocked.Increment(
                ref pathDistanceBuckets[(int)GetDistanceBucket(distance)]);

            ushort constraints = (ushort)observation.Constraints;
            for (int index = 0;
                 index < PathRequestCatalog.ConstraintCount;
                 index++)
            {
                if ((constraints & (1 << index)) != 0)
                {
                    Interlocked.Increment(ref pathConstraintCounts[index]);
                }
            }

            ObserveTargetReuse(observation.TargetKey, observation.Tick);
        }

        internal void ObservePathDataUpdate(
            in PathSpatialObservation observation)
        {
            Interlocked.Increment(ref pathDataUpdates);
            Interlocked.Add(ref pathDirtyCells, observation.DirtyCells);
            Interlocked.Add(
                ref pathExpandedCellVisits,
                observation.ExpandedCellVisits);
            Interlocked.Add(
                ref pathUniqueExpandedCells,
                observation.UniqueExpandedCells);
            Interlocked.Add(ref pathChunks8, observation.Chunks8);
            Interlocked.Add(ref pathChunks16, observation.Chunks16);
            Interlocked.Add(ref pathChunks32, observation.Chunks32);
            UpdateMaximum(ref pathMaximumDirtyCells, observation.DirtyCells);
            UpdateMaximum(
                ref pathMaximumUniqueExpandedCells,
                observation.UniqueExpandedCells);
            UpdateMaximum(ref pathMaximumChunks8, observation.Chunks8);
            UpdateMaximum(ref pathMaximumChunks16, observation.Chunks16);
            UpdateMaximum(ref pathMaximumChunks32, observation.Chunks32);
        }

        internal void ObserveReachabilityCache(bool hit)
        {
            if (hit)
            {
                Interlocked.Increment(ref reachabilityCacheHits);
            }
            else
            {
                Interlocked.Increment(ref reachabilityCacheMisses);
            }
        }

        internal RuntimeProfilingSnapshot CaptureRuntimeProfiling(
            bool publish = false) =>
            new(
                runtimeProfiler.AggregationMode,
                publish
                    ? runtimeProfiler.PublishSnapshot()
                    : runtimeProfiler.PublishedSnapshot,
                new RuntimePathfindingSnapshot(
                    Interlocked.Read(ref pathBatches),
                    Interlocked.Read(ref pathRequests),
                    Interlocked.Read(ref pathMaximumBatchSize),
                    Interlocked.Read(ref pathTotalQueueDelayTicks),
                    Interlocked.Read(ref pathMaximumQueueDelayTicks),
                    Interlocked.Read(ref pathDataUpdates),
                    Interlocked.Read(ref pathDirtyCells),
                    Interlocked.Read(ref pathMaximumDirtyCells),
                    Interlocked.Read(ref pathGridJobsCreated),
                    Interlocked.Read(ref reachabilityCacheHits),
                    Interlocked.Read(ref reachabilityCacheMisses),
                    new RuntimePathRequestSnapshot(
                        Interlocked.Read(ref pathRequestObservations),
                        Interlocked.Read(ref pathRepeatedTargets),
                        Interlocked.Read(ref pathTargetTrackerCollisions),
                        Interlocked.Read(ref pathTotalDistance),
                        Interlocked.Read(ref pathMaximumDistance),
                        CaptureCounters(pathPawnCategories),
                        CaptureCounters(pathTraversalModes),
                        CaptureCounters(pathEndModes),
                        CaptureCounters(pathTargetKinds),
                        CaptureCounters(pathDistanceBuckets),
                        CaptureCounters(pathConstraintCounts)),
                    new RuntimeSpatialSnapshot(
                        Interlocked.Read(ref pathExpandedCellVisits),
                        Interlocked.Read(ref pathUniqueExpandedCells),
                        Interlocked.Read(ref pathChunks8),
                        Interlocked.Read(ref pathChunks16),
                        Interlocked.Read(ref pathChunks32),
                        Interlocked.Read(ref pathMaximumUniqueExpandedCells),
                        Interlocked.Read(ref pathMaximumChunks8),
                        Interlocked.Read(ref pathMaximumChunks16),
                        Interlocked.Read(ref pathMaximumChunks32))));

        internal RuntimeDiagnosticsSnapshot Complete(
            string source,
            TextureDdsCacheSnapshot ddsCache,
            RuntimeSchedulerSnapshot scheduler,
            SystemMemorySnapshot memory)
        {
            PlayDataTelemetrySnapshot loading;
            Profiler<PlayDataLoadStage> completedProfiler;
            double observedMilliseconds;
            lock (sync)
            {
                if (!active)
                {
                    return null;
                }

                long completedAt = Stopwatch.GetTimestamp();
                observedMilliseconds = ToMilliseconds(
                    Math.Max(0L, completedAt - startedAt));
                ProfileSnapshot<PlayDataLoadStage> stages =
                    profiler.Snapshot();
                loading = new PlayDataTelemetrySnapshot(
                    observedMilliseconds,
                    CaptureStages(stages));
                active = false;
                stagesComplete = false;
                stageSlots = null;
                completedProfiler = profiler;
                profiler = null;
                startedAt = 0L;
                stageStartedAt = 0L;
                Volatile.Write(ref liveState, null);
            }

            completedProfiler.Dispose();
            LoadingEstimateStore.Write(observedMilliseconds);
            return new RuntimeDiagnosticsSnapshot(
                source,
                PreloaderTimelineState.GetSnapshot(),
                DdsCacheContract.CaptureReadAhead(),
                loading,
                ddsCache,
                scheduler,
                memory);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            Abort();
            runtimeProfiler.Dispose();
        }

        private static List<PlayDataStageMeasurement> CaptureStages(
            ProfileSnapshot<PlayDataLoadStage> stages)
        {
            List<PlayDataStageMeasurement> measurements =
                new(stages.Count);
            foreach (ProfileMeasurement<PlayDataLoadStage> stage in stages)
            {
                measurements.Add(new PlayDataStageMeasurement(stage));
            }

            return measurements;
        }

        private void StartStage(PlayDataLoadStage next, long now)
        {
            stage = next;
            stageStartedAt = now;
        }

        private void FinishStage(long now, bool succeeded)
        {
            stageSlots[(int)stage - 1].ObserveStopwatchTicks(
                Math.Max(0L, now - stageStartedAt),
                succeeded);
        }

        private void PublishLiveState() =>
            Volatile.Write(
                ref liveState,
                new LoadingLiveState(
                    stage,
                    startedAt,
                    estimatedDurationMilliseconds));

        private static ProfileSlot<PlayDataLoadStage>[] CreateStageSlots(
            Profiler<PlayDataLoadStage> profiler)
        {
            var slots =
                new ProfileSlot<PlayDataLoadStage>[
                    PlayDataLoadStageCatalog.Count];
            for (int number = 1; number <= slots.Length; number++)
            {
                var stage = (PlayDataLoadStage)number;
                slots[number - 1] = profiler.GetSlot(stage);
            }

            return slots;
        }

        private static ProfileSlot<RuntimeHotpath>[] CreateRuntimeSlots(
            Profiler<RuntimeHotpath> profiler)
        {
            var slots =
                new ProfileSlot<RuntimeHotpath>[RuntimeHotpathCatalog.Count];
            for (int index = 0; index < slots.Length; index++)
            {
                slots[index] = profiler.GetSlot((RuntimeHotpath)index);
            }

            return slots;
        }

        private void ObserveTargetReuse(long targetKey, int tick)
        {
            int index = (int)((ulong)targetKey ^
                              ((ulong)targetKey >> 32)) &
                        TargetHistoryMask;
            long previousKey = Interlocked.Read(
                ref pathTargetHistoryKeys[index]);
            if (previousKey == targetKey)
            {
                int previousTick = Interlocked.Exchange(
                    ref pathTargetHistoryTicks[index],
                    tick);
                int elapsed = tick - previousTick;
                if (elapsed >= 0 && elapsed <= TargetReuseWindowTicks)
                {
                    Interlocked.Increment(ref pathRepeatedTargets);
                }

                return;
            }

            if (previousKey != 0L)
            {
                Interlocked.Increment(ref pathTargetTrackerCollisions);
            }

            Interlocked.Exchange(ref pathTargetHistoryKeys[index], targetKey);
            Volatile.Write(ref pathTargetHistoryTicks[index], tick);
        }

        private static PathRequestDistanceBucket GetDistanceBucket(
            int distance)
        {
            if (distance <= 16)
            {
                return PathRequestDistanceBucket.UpTo16;
            }

            if (distance <= 32)
            {
                return PathRequestDistanceBucket.UpTo32;
            }

            if (distance <= 64)
            {
                return PathRequestDistanceBucket.UpTo64;
            }

            return distance <= 128
                ? PathRequestDistanceBucket.UpTo128
                : PathRequestDistanceBucket.Over128;
        }

        private static long[] CaptureCounters(long[] counters)
        {
            var snapshot = new long[counters.Length];
            for (int index = 0; index < snapshot.Length; index++)
            {
                snapshot[index] = Interlocked.Read(ref counters[index]);
            }

            return snapshot;
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long current = Interlocked.Read(ref target);
            while (value > current)
            {
                long observed = Interlocked.CompareExchange(
                    ref target,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }

        private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private sealed class LoadingLiveState
        {
            internal LoadingLiveState(
                PlayDataLoadStage stage,
                long startedAt,
                double estimatedDurationMilliseconds)
            {
                Stage = stage;
                StartedAt = startedAt;
                EstimatedDurationMilliseconds = estimatedDurationMilliseconds;
            }

            internal PlayDataLoadStage Stage { get; }

            internal long StartedAt { get; }

            internal double EstimatedDurationMilliseconds { get; }
        }
    }

    [DataContract]
    internal sealed class PlayDataTelemetrySnapshot
    {
        internal PlayDataTelemetrySnapshot(
            double observedMilliseconds,
            List<PlayDataStageMeasurement> stages)
        {
            ObservedMilliseconds = observedMilliseconds;
            Stages = stages ?? throw new ArgumentNullException(nameof(stages));
        }

        [DataMember(Name = "observedMs", Order = 1)]
        internal double ObservedMilliseconds { get; private set; }

        [DataMember(Name = "stages", Order = 2)]
        internal List<PlayDataStageMeasurement> Stages { get; private set; }
    }

    [DataContract]
    internal sealed class PlayDataStageMeasurement
    {
        internal PlayDataStageMeasurement(
            ProfileMeasurement<PlayDataLoadStage> measurement)
        {
            PlayDataLoadStage stage = measurement.Key;
            Id = stage.ToString();
            Number = (int)stage;
            Name = PlayDataLoadStageCatalog.GetName(stage);
            ElapsedMilliseconds = measurement.TotalTime.TotalMilliseconds;
            Calls = measurement.Calls;
            Failures = measurement.Failures;
        }

        [DataMember(Name = "id", Order = 1)]
        internal string Id { get; private set; }

        [DataMember(Name = "number", Order = 2)]
        internal int Number { get; private set; }

        [DataMember(Name = "name", Order = 3)]
        internal string Name { get; private set; }

        [DataMember(Name = "elapsedMs", Order = 4)]
        internal double ElapsedMilliseconds { get; private set; }

        [DataMember(Name = "calls", Order = 5)]
        internal long Calls { get; private set; }

        [DataMember(Name = "failures", Order = 6)]
        internal long Failures { get; private set; }
    }
}
