using System;
using System.Diagnostics;
using System.Threading;
using FixWorld.Events;
using Verse;

namespace FixWorld.PlayData
{
    internal sealed class PlayDataStageRunner
    {
        private readonly EventBus events;

        internal PlayDataStageRunner(EventBus events)
        {
            this.events = events ?? throw new ArgumentNullException(nameof(events));
        }

        internal void Run(PlayDataLoadStage stage, Action execute)
        {
            Run(
                stage,
                () =>
                {
                    execute();
                    return false;
                });
        }

        internal TResult Run<TResult>(
            PlayDataLoadStage stage,
            Func<TResult> execute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            using (PlayDataStageOperation operation = Begin(stage))
            {
                try
                {
                    TResult result = execute();
                    operation.Complete();
                    return result;
                }
                catch
                {
                    operation.Fail();
                    throw;
                }
            }
        }

        internal PlayDataStageOperation Begin(PlayDataLoadStage stage)
        {
            LongEventHandler.SetCurrentEventText(
                "FixWorld: " + PlayDataLoadStageCatalog.GetName(stage));
            return new PlayDataStageOperation(events, stage);
        }
    }

    internal sealed class PlayDataStageOperation : IDisposable
    {
        private readonly EventBus events;
        private readonly bool mainThread;
        private readonly int managedThreadId;
        private readonly StageResourceSample resourcesAtStart;
        private readonly Stopwatch stopwatch;
        private int terminal;

        internal PlayDataStageOperation(
            EventBus events,
            PlayDataLoadStage stage)
        {
            this.events = events;
            Stage = stage;
            mainThread = UnityData.IsInMainThread;
            managedThreadId = Thread.CurrentThread.ManagedThreadId;
            resourcesAtStart = StageResourceSample.Capture();
            stopwatch = Stopwatch.StartNew();
            Publish(PlayDataLoadStageEventKind.Started, null);
        }

        internal PlayDataLoadStage Stage { get; }

        internal void Report(string activity)
        {
            if (Volatile.Read(ref terminal) == 0)
            {
                events.PublishLatest(
                    "play-data-progress",
                    Create(PlayDataLoadStageEventKind.Progress, activity));
            }
        }

        internal void Complete()
        {
            Finish(PlayDataLoadStageEventKind.Completed);
        }

        internal void Fail()
        {
            Finish(PlayDataLoadStageEventKind.Failed);
        }

        public void Dispose()
        {
            Complete();
        }

        private void Finish(PlayDataLoadStageEventKind kind)
        {
            if (Interlocked.Exchange(ref terminal, 1) != 0)
            {
                return;
            }

            stopwatch.Stop();
            Publish(kind, null);
        }

        private void Publish(
            PlayDataLoadStageEventKind kind,
            string activity)
        {
            events.Publish(Create(kind, activity));
        }

        private PlayDataLoadStageEvent Create(
            PlayDataLoadStageEventKind kind,
            string activity)
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

            return new PlayDataLoadStageEvent(
                Stage,
                kind,
                stopwatch.Elapsed,
                activity,
                diagnostics);
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
