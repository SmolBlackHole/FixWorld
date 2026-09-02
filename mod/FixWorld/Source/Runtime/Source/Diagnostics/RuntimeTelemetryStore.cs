using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using FixWorld.Events;
using FixWorld.PlayData;
using FixWorld.Preloader;
using FixWorld.Profiling;
using FixWorld.Textures;

namespace FixWorld.Diagnostics
{
    internal sealed class RuntimeTelemetryStore : IDisposable
    {
        private const int DeferredHotpathCount = 20;

        private readonly IDisposable operationSubscription;
        private readonly IDisposable stageSubscription;
        private readonly List<PlayDataOperationMeasurement> operations =
            new List<PlayDataOperationMeasurement>();
        private readonly List<PlayDataStageMeasurement> stages =
            new List<PlayDataStageMeasurement>();
        private Profiler<DeferredWorkKey> deferredRuntimes =
            new Profiler<DeferredWorkKey>();
        private Profiler<DeferredWorkKey> deferredWaits =
            new Profiler<DeferredWorkKey>();
        private long startedAt;

        internal RuntimeTelemetryStore(EventBus events)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            stageSubscription = events.Subscribe<PlayDataLoadStageEvent>(
                ObserveStage);
            operationSubscription = events.Subscribe<PlayDataOperationEvent>(
                ObserveOperation);
        }

        internal RuntimeDiagnosticsSnapshot Snapshot { get; private set; }

        internal void Start()
        {
            stages.Clear();
            operations.Clear();
            deferredRuntimes = new Profiler<DeferredWorkKey>();
            deferredWaits = new Profiler<DeferredWorkKey>();
            startedAt = Stopwatch.GetTimestamp();
        }

        internal void Abort()
        {
            stages.Clear();
            operations.Clear();
            deferredRuntimes = new Profiler<DeferredWorkKey>();
            deferredWaits = new Profiler<DeferredWorkKey>();
            startedAt = 0L;
        }

        internal void ObserveDeferred(
            string owner,
            string name,
            TimeSpan waitTime,
            TimeSpan runTime,
            bool succeeded)
        {
            DeferredWorkKey key = new DeferredWorkKey(owner, name);
            deferredWaits.Observe(key, waitTime);
            deferredRuntimes.Observe(key, runTime, succeeded);
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
                CaptureDeferred(),
                scheduler,
                memory,
                detailedCaptureEnabled);
            return Snapshot;
        }

        public void Dispose()
        {
            operationSubscription.Dispose();
            stageSubscription.Dispose();
        }

        private PlayDataTelemetrySnapshot CaptureStages(long completedAt)
        {
            double observedMilliseconds = startedAt == 0L
                ? 0.0
                : ToMilliseconds(Math.Max(0L, completedAt - startedAt));
            return new PlayDataTelemetrySnapshot(
                observedMilliseconds,
                stages.OrderBy(item => item.Number).ToList(),
                operations.ToList());
        }

        private DeferredWorkSnapshot CaptureDeferred()
        {
            ProfileSnapshot<DeferredWorkKey> waitSnapshot =
                deferredWaits.Snapshot();
            List<DeferredWorkMeasurement> measurements = deferredRuntimes
                .Snapshot()
                .Select(runtime =>
                {
                    waitSnapshot.TryGet(
                        runtime.Key,
                        out ProfileMeasurement<DeferredWorkKey> wait);
                    return new DeferredWorkMeasurement(
                        runtime.Key.Owner,
                        runtime.Key.Name,
                        runtime.Calls,
                        runtime.Failures,
                        runtime.TotalTime.TotalMilliseconds,
                        runtime.MaximumTime.TotalMilliseconds,
                        wait?.AverageTime.TotalMilliseconds ?? 0.0,
                        wait?.MaximumTime.TotalMilliseconds ?? 0.0);
                })
                .ToList();
            return new DeferredWorkSnapshot(
                measurements.Sum(item => item.Calls),
                measurements.Sum(item => item.Failures),
                measurements.Sum(item => item.TotalMilliseconds),
                measurements.Count == 0
                    ? 0.0
                    : measurements.Max(item => item.MaximumWaitMilliseconds),
                measurements
                    .OrderByDescending(item => item.TotalMilliseconds)
                    .ThenBy(item => item.Owner, StringComparer.Ordinal)
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .Take(DeferredHotpathCount)
                    .ToList());
        }

        private void ObserveStage(PlayDataLoadStageEvent stageEvent)
        {
            if (stageEvent.Kind == PlayDataLoadStageEventKind.Completed)
            {
                PlayDataStageDiagnostics diagnostics = stageEvent.Diagnostics;
                stages.Add(new PlayDataStageMeasurement(
                    stageEvent.Stage,
                    stageEvent.Elapsed.TotalMilliseconds,
                    diagnostics));
            }
        }

        private void ObserveOperation(PlayDataOperationEvent operationEvent)
        {
            operations.Add(new PlayDataOperationMeasurement(
                operationEvent.Stage,
                operationEvent.Name,
                operationEvent.Elapsed.TotalMilliseconds,
                operationEvent.Succeeded));
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private readonly struct DeferredWorkKey : IEquatable<DeferredWorkKey>
        {
            internal DeferredWorkKey(string owner, string name)
            {
                Owner = owner ?? "global";
                Name = name ?? "unknown";
            }

            internal string Owner { get; }

            internal string Name { get; }

            public bool Equals(DeferredWorkKey other)
            {
                return string.Equals(Owner, other.Owner, StringComparison.Ordinal) &&
                       string.Equals(Name, other.Name, StringComparison.Ordinal);
            }

            public override bool Equals(object value)
            {
                return value is DeferredWorkKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Owner != null ? Owner.GetHashCode() : 0) * 397) ^
                           (Name != null ? Name.GetHashCode() : 0);
                }
            }
        }
    }

    [DataContract]
    internal sealed class PlayDataTelemetrySnapshot
    {
        internal PlayDataTelemetrySnapshot(
            double observedMilliseconds,
            IReadOnlyList<PlayDataStageMeasurement> stages,
            IReadOnlyList<PlayDataOperationMeasurement> operations)
        {
            ObservedMilliseconds = observedMilliseconds;
            Stages = new List<PlayDataStageMeasurement>(
                stages ?? throw new ArgumentNullException(nameof(stages)));
            Operations = new List<PlayDataOperationMeasurement>(
                operations ?? throw new ArgumentNullException(nameof(operations)));
        }

        [DataMember(Name = "observedMs", Order = 1)]
        internal double ObservedMilliseconds { get; private set; }

        [DataMember(Name = "stages", Order = 2)]
        internal List<PlayDataStageMeasurement> Stages { get; private set; }

        [DataMember(Name = "operations", Order = 3)]
        internal List<PlayDataOperationMeasurement> Operations { get; private set; }
    }

    [DataContract]
    internal sealed class PlayDataOperationMeasurement
    {
        internal PlayDataOperationMeasurement(
            PlayDataLoadStage stage,
            string name,
            double elapsedMilliseconds,
            bool succeeded)
        {
            StageId = stage.ToString();
            StageNumber = (int)stage;
            Name = name;
            ElapsedMilliseconds = elapsedMilliseconds;
            Succeeded = succeeded;
        }

        [DataMember(Name = "stageId", Order = 1)]
        internal string StageId { get; private set; }

        [DataMember(Name = "stageNumber", Order = 2)]
        internal int StageNumber { get; private set; }

        [DataMember(Name = "name", Order = 3)]
        internal string Name { get; private set; }

        [DataMember(Name = "elapsedMs", Order = 4)]
        internal double ElapsedMilliseconds { get; private set; }

        [DataMember(Name = "succeeded", Order = 5)]
        internal bool Succeeded { get; private set; }
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

    [DataContract]
    internal sealed class DeferredWorkSnapshot
    {
        internal DeferredWorkSnapshot(
            long calls,
            long failures,
            double runtimeMilliseconds,
            double maximumQueueDelayMilliseconds,
            IReadOnlyList<DeferredWorkMeasurement> top)
        {
            Calls = calls;
            Failures = failures;
            RuntimeMilliseconds = runtimeMilliseconds;
            MaximumQueueDelayMilliseconds = maximumQueueDelayMilliseconds;
            Top = new List<DeferredWorkMeasurement>(
                top ?? throw new ArgumentNullException(nameof(top)));
        }

        [DataMember(Name = "calls", Order = 1)]
        internal long Calls { get; private set; }

        [DataMember(Name = "failures", Order = 2)]
        internal long Failures { get; private set; }

        [DataMember(Name = "runtimeMs", Order = 3)]
        internal double RuntimeMilliseconds { get; private set; }

        [DataMember(Name = "maxQueueDelayMs", Order = 4)]
        internal double MaximumQueueDelayMilliseconds { get; private set; }

        [DataMember(Name = "top", Order = 5)]
        internal List<DeferredWorkMeasurement> Top { get; private set; }
    }

    [DataContract]
    internal sealed class DeferredWorkMeasurement
    {
        internal DeferredWorkMeasurement(
            string owner,
            string name,
            long calls,
            long failures,
            double totalMilliseconds,
            double maximumMilliseconds,
            double averageWaitMilliseconds,
            double maximumWaitMilliseconds)
        {
            Owner = owner;
            Name = name;
            Calls = calls;
            Failures = failures;
            TotalMilliseconds = totalMilliseconds;
            MaximumMilliseconds = maximumMilliseconds;
            AverageWaitMilliseconds = averageWaitMilliseconds;
            MaximumWaitMilliseconds = maximumWaitMilliseconds;
        }

        [DataMember(Name = "owner", Order = 1)]
        internal string Owner { get; private set; }

        [DataMember(Name = "name", Order = 2)]
        internal string Name { get; private set; }

        [DataMember(Name = "calls", Order = 3)]
        internal long Calls { get; private set; }

        [DataMember(Name = "failures", Order = 4)]
        internal long Failures { get; private set; }

        [DataMember(Name = "totalMs", Order = 5)]
        internal double TotalMilliseconds { get; private set; }

        [DataMember(Name = "maxMs", Order = 6)]
        internal double MaximumMilliseconds { get; private set; }

        [DataMember(Name = "averageWaitMs", Order = 7)]
        internal double AverageWaitMilliseconds { get; private set; }

        [DataMember(Name = "maxWaitMs", Order = 8)]
        internal double MaximumWaitMilliseconds { get; private set; }
    }
}
