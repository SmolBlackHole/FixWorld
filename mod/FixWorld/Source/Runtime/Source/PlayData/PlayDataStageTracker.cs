using System;
using System.Diagnostics;
using System.Threading;
using FixWorld.Events;
using Verse;

namespace FixWorld.PlayData
{
    internal sealed class PlayDataStageTracker
    {
        private readonly object sync = new object();
        private readonly EventBus events;
        private PlayDataStageOperation current;
        private bool active;
        private int generation;

        internal PlayDataStageTracker(EventBus events)
        {
            this.events = events ?? throw new ArgumentNullException(nameof(events));
        }

        internal void Start(int runGeneration)
        {
            lock (sync)
            {
                if (active)
                {
                    return;
                }

                active = true;
                generation = runGeneration;
                current = new PlayDataStageOperation(
                    events,
                    generation,
                    PlayDataLoadStage.Reset);
            }
        }

        internal bool Transition(PlayDataLoadStage stage)
        {
            lock (sync)
            {
                if (!active || current == null ||
                    (int)stage != (int)current.Stage + 1)
                {
                    return false;
                }

                current?.Complete();
                current = new PlayDataStageOperation(events, generation, stage);
                return true;
            }
        }

        internal void Complete()
        {
            lock (sync)
            {
                if (!active)
                {
                    return;
                }

                current?.Complete();
                current = new PlayDataStageOperation(
                    events,
                    generation,
                    PlayDataLoadStage.Complete);
                current.Complete();
                current = null;
                active = false;
            }
        }

        internal void Fail()
        {
            lock (sync)
            {
                current?.Fail();
                current = null;
                active = false;
            }
        }
    }

    internal sealed class PlayDataStageOperation
    {
        private readonly EventBus events;
        private readonly bool mainThread;
        private readonly int managedThreadId;
        private readonly StageResourceSample resourcesAtStart;
        private readonly Stopwatch stopwatch;
        private int terminal;

        internal PlayDataStageOperation(
            EventBus events,
            int generation,
            PlayDataLoadStage stage)
        {
            this.events = events;
            Generation = generation;
            Stage = stage;
            mainThread = UnityData.IsInMainThread;
            managedThreadId = Thread.CurrentThread.ManagedThreadId;
            resourcesAtStart = StageResourceSample.Capture();
            stopwatch = Stopwatch.StartNew();
            Publish(PlayDataLoadStageEventKind.Started);
        }

        internal PlayDataLoadStage Stage { get; }

        private int Generation { get; }

        internal void Complete()
        {
            Finish(PlayDataLoadStageEventKind.Completed);
        }

        internal void Fail()
        {
            Finish(PlayDataLoadStageEventKind.Failed);
        }

        private void Finish(PlayDataLoadStageEventKind kind)
        {
            if (Interlocked.Exchange(ref terminal, 1) != 0)
            {
                return;
            }

            stopwatch.Stop();
            Publish(kind);
        }

        private void Publish(PlayDataLoadStageEventKind kind)
        {
            PlayDataStageDiagnostics diagnostics =
                new PlayDataStageDiagnostics(
                    resourceMetricsAvailable: false,
                    mainThread,
                    managedThreadId,
                    TimeSpan.Zero,
                    managedHeapDeltaBytes: 0L,
                    workingSetDeltaBytes: 0L,
                    generationZeroCollections: 0,
                    generationOneCollections: 0,
                    generationTwoCollections: 0);
            if (kind == PlayDataLoadStageEventKind.Completed ||
                kind == PlayDataLoadStageEventKind.Failed)
            {
                diagnostics = resourcesAtStart.CreateDiagnostics(
                    mainThread,
                    managedThreadId,
                    StageResourceSample.Capture());
            }

            events.Publish(new PlayDataLoadStageEvent(
                Generation,
                Stage,
                kind,
                stopwatch.Elapsed,
                activity: null,
                diagnostics));
        }

        private readonly struct StageResourceSample
        {
            private StageResourceSample(
                bool available,
                long processCpuTicks,
                long managedHeapBytes,
                long workingSetBytes,
                int generationZeroCollections,
                int generationOneCollections,
                int generationTwoCollections)
            {
                Available = available;
                ProcessCpuTicks = processCpuTicks;
                ManagedHeapBytes = managedHeapBytes;
                WorkingSetBytes = workingSetBytes;
                GenerationZeroCollections = generationZeroCollections;
                GenerationOneCollections = generationOneCollections;
                GenerationTwoCollections = generationTwoCollections;
            }

            private bool Available { get; }

            private long ProcessCpuTicks { get; }

            private long ManagedHeapBytes { get; }

            private long WorkingSetBytes { get; }

            private int GenerationZeroCollections { get; }

            private int GenerationOneCollections { get; }

            private int GenerationTwoCollections { get; }

            internal static StageResourceSample Capture()
            {
                try
                {
                    using (Process process = Process.GetCurrentProcess())
                    {
                        process.Refresh();
                        return new StageResourceSample(
                            available: true,
                            process.TotalProcessorTime.Ticks,
                            GC.GetTotalMemory(forceFullCollection: false),
                            process.WorkingSet64,
                            GC.CollectionCount(0),
                            GC.CollectionCount(1),
                            GC.CollectionCount(2));
                    }
                }
                catch
                {
                    return new StageResourceSample(
                        available: false,
                        processCpuTicks: 0L,
                        managedHeapBytes: 0L,
                        workingSetBytes: 0L,
                        generationZeroCollections: 0,
                        generationOneCollections: 0,
                        generationTwoCollections: 0);
                }
            }

            internal PlayDataStageDiagnostics CreateDiagnostics(
                bool isMainThread,
                int threadId,
                StageResourceSample completed)
            {
                bool available = Available && completed.Available;
                return new PlayDataStageDiagnostics(
                    available,
                    isMainThread,
                    threadId,
                    available
                        ? TimeSpan.FromTicks(Math.Max(
                            0L,
                            completed.ProcessCpuTicks - ProcessCpuTicks))
                        : TimeSpan.Zero,
                    available
                        ? completed.ManagedHeapBytes - ManagedHeapBytes
                        : 0L,
                    available
                        ? completed.WorkingSetBytes - WorkingSetBytes
                        : 0L,
                    available
                        ? Math.Max(
                            0,
                            completed.GenerationZeroCollections -
                            GenerationZeroCollections)
                        : 0,
                    available
                        ? Math.Max(
                            0,
                            completed.GenerationOneCollections -
                            GenerationOneCollections)
                        : 0,
                    available
                        ? Math.Max(
                            0,
                            completed.GenerationTwoCollections -
                            GenerationTwoCollections)
                        : 0);
            }
        }
    }
}
