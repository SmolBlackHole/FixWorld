using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using FixWorld.Loading;
using FixWorld.PlayData;
using FixWorld.Preloader;
using FixWorld.Profiling;
using FixWorld.Textures;

namespace FixWorld.Diagnostics
{
    internal sealed class RuntimeTelemetryStore
    {
        private readonly object sync = new object();

        private bool active;
        private double estimatedDurationMilliseconds;
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
                Profiler<PlayDataLoadStage> profiler =
                    new Profiler<PlayDataLoadStage>();
                stageSlots = CreateStageSlots(profiler);
                startedAt = Stopwatch.GetTimestamp();
                estimatedDurationMilliseconds = LoadingEstimateStore.Read();
                StartStage(PlayDataLoadStage.Reset, startedAt);
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
                return true;
            }
        }

        internal bool Abort()
        {
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
                startedAt = 0L;
                stageStartedAt = 0L;
                return true;
            }
        }

        internal bool TryGetLoadingSnapshot(
            out PlayDataLoadingSnapshot snapshot)
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
                    estimatedDurationMilliseconds);
                return true;
            }
        }

        internal RuntimeDiagnosticsSnapshot Complete(
            string source,
            TextureDdsCacheSnapshot ddsCache,
            RuntimeSchedulerSnapshot scheduler,
            SystemMemorySnapshot memory)
        {
            PlayDataTelemetrySnapshot loading;
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
                loading = new PlayDataTelemetrySnapshot(
                    observedMilliseconds,
                    CaptureStages());
                active = false;
                stagesComplete = false;
                stageSlots = null;
                startedAt = 0L;
                stageStartedAt = 0L;
            }

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

        private List<PlayDataStageMeasurement> CaptureStages()
        {
            List<PlayDataStageMeasurement> measurements =
                new List<PlayDataStageMeasurement>(stageSlots.Length);
            foreach (ProfileSlot<PlayDataLoadStage> slot in stageSlots)
            {
                measurements.Add(
                    new PlayDataStageMeasurement(slot.Snapshot()));
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
            TimeSpan elapsed = TimeSpan.FromSeconds(
                (double)Math.Max(0L, now - stageStartedAt) /
                Stopwatch.Frequency);
            stageSlots[(int)stage - 1].Observe(elapsed, succeeded);
        }

        private static ProfileSlot<PlayDataLoadStage>[] CreateStageSlots(
            Profiler<PlayDataLoadStage> profiler)
        {
            ProfileSlot<PlayDataLoadStage>[] slots =
                new ProfileSlot<PlayDataLoadStage>[
                    PlayDataLoadStageCatalog.Count];
            for (int number = 1; number <= slots.Length; number++)
            {
                PlayDataLoadStage stage = (PlayDataLoadStage)number;
                slots[number - 1] = profiler.GetSlot(stage);
            }

            return slots;
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
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
