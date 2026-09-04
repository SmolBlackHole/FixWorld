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
        private readonly object sync = new();

        private bool active;
        private double estimatedDurationMilliseconds;
        private LoadingLiveState liveState;
        private Profiler<PlayDataLoadStage> profiler;
        private ProfileSlot<PlayDataLoadStage>[] stageSlots;
        private PlayDataLoadStage stage;
        private long stageStartedAt;
        private long startedAt;
        private bool stagesComplete;

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

        public void Dispose() => Abort();

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
