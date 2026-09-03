using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using FixWorld.Events;
using FixWorld.PlayData;
using FixWorld.Preloader;
using FixWorld.Textures;

namespace FixWorld.Diagnostics
{
    internal sealed class RuntimeTelemetryStore : IDisposable
    {
        private readonly IDisposable stageSubscription;
        private readonly List<PlayDataStageMeasurement> stages =
            new List<PlayDataStageMeasurement>();
        private int generation;
        private long startedAt;

        internal RuntimeTelemetryStore(EventBus events)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            stageSubscription = events.Subscribe<PlayDataLoadStageEvent>(
                ObserveStage);
        }

        internal RuntimeDiagnosticsSnapshot Snapshot { get; private set; }

        internal void Start(int runGeneration)
        {
            stages.Clear();
            generation = runGeneration;
            startedAt = Stopwatch.GetTimestamp();
        }

        internal void Abort()
        {
            stages.Clear();
            startedAt = 0L;
        }

        internal RuntimeDiagnosticsSnapshot Complete(
            string source,
            TextureProbeSnapshot textures,
            TextureDdsCacheSnapshot ddsCache,
            RuntimeSchedulerSnapshot scheduler,
            SystemMemorySnapshot memory,
            bool detailedCaptureEnabled)
        {
            long completedAt = Stopwatch.GetTimestamp();
            Snapshot = new RuntimeDiagnosticsSnapshot(
                source,
                PreloaderTimelineState.GetSnapshot(),
                DdsCacheContract.CaptureReadAhead(),
                CaptureStages(completedAt),
                textures,
                ddsCache,
                scheduler,
                memory,
                detailedCaptureEnabled);
            return Snapshot;
        }

        public void Dispose()
        {
            stageSubscription.Dispose();
        }

        private PlayDataTelemetrySnapshot CaptureStages(long completedAt)
        {
            double observedMilliseconds = startedAt == 0L
                ? 0.0
                : ToMilliseconds(Math.Max(0L, completedAt - startedAt));
            return new PlayDataTelemetrySnapshot(
                observedMilliseconds,
                stages.OrderBy(item => item.Number).ToList());
        }

        private void ObserveStage(PlayDataLoadStageEvent stageEvent)
        {
            if (stageEvent.Generation == generation &&
                stageEvent.Kind == PlayDataLoadStageEventKind.Completed)
            {
                stages.Add(new PlayDataStageMeasurement(
                    stageEvent.Stage,
                    stageEvent.Elapsed.TotalMilliseconds,
                    stageEvent.Diagnostics));
            }
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
            IReadOnlyList<PlayDataStageMeasurement> stages)
        {
            ObservedMilliseconds = observedMilliseconds;
            Stages = new List<PlayDataStageMeasurement>(
                stages ?? throw new ArgumentNullException(nameof(stages)));
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
            PlayDataLoadStage stage,
            double elapsedMilliseconds,
            PlayDataStageDiagnostics diagnostics)
        {
            Id = stage.ToString();
            Number = (int)stage;
            Name = PlayDataLoadStageCatalog.GetName(stage);
            ElapsedMilliseconds = elapsedMilliseconds;
            Thread = diagnostics.MainThread ? "main" : "worker";
            ManagedThreadId = diagnostics.ManagedThreadId;
            ResourceMetricsAvailable = diagnostics.ResourceMetricsAvailable;
            ProcessCpuMilliseconds =
                diagnostics.ProcessCpuTime.TotalMilliseconds;
            CpuCoreEquivalent = elapsedMilliseconds <= 0.0
                ? 0.0
                : ProcessCpuMilliseconds / elapsedMilliseconds;
            ManagedHeapDeltaBytes = diagnostics.ManagedHeapDeltaBytes;
            WorkingSetDeltaBytes = diagnostics.WorkingSetDeltaBytes;
            GenerationZeroCollections = diagnostics.GenerationZeroCollections;
            GenerationOneCollections = diagnostics.GenerationOneCollections;
            GenerationTwoCollections = diagnostics.GenerationTwoCollections;
        }

        [DataMember(Name = "id", Order = 1)]
        internal string Id { get; private set; }

        [DataMember(Name = "number", Order = 2)]
        internal int Number { get; private set; }

        [DataMember(Name = "name", Order = 3)]
        internal string Name { get; private set; }

        [DataMember(Name = "elapsedMs", Order = 4)]
        internal double ElapsedMilliseconds { get; private set; }

        [DataMember(Name = "thread", Order = 5)]
        internal string Thread { get; private set; }

        [DataMember(Name = "threadId", Order = 6)]
        internal int ManagedThreadId { get; private set; }

        [DataMember(Name = "resourceMetricsAvailable", Order = 7)]
        internal bool ResourceMetricsAvailable { get; private set; }

        [DataMember(Name = "processCpuMs", Order = 8)]
        internal double ProcessCpuMilliseconds { get; private set; }

        [DataMember(Name = "cpuCoreEquivalent", Order = 9)]
        internal double CpuCoreEquivalent { get; private set; }

        [DataMember(Name = "managedHeapDeltaBytes", Order = 10)]
        internal long ManagedHeapDeltaBytes { get; private set; }

        [DataMember(Name = "workingSetDeltaBytes", Order = 11)]
        internal long WorkingSetDeltaBytes { get; private set; }

        [DataMember(Name = "gen0Collections", Order = 12)]
        internal int GenerationZeroCollections { get; private set; }

        [DataMember(Name = "gen1Collections", Order = 13)]
        internal int GenerationOneCollections { get; private set; }

        [DataMember(Name = "gen2Collections", Order = 14)]
        internal int GenerationTwoCollections { get; private set; }
    }
}
