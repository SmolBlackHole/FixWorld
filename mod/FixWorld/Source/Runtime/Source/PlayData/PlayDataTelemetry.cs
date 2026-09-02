using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FixWorld.Events;
using FixWorld.Loading;
using FixWorld.Profiling;

namespace FixWorld.PlayData
{
    internal sealed class PlayDataTelemetry : IDisposable
    {
        private Profiler<PlayDataLoadStage> profiler =
            new Profiler<PlayDataLoadStage>();
        private readonly IDisposable subscription;
        private long startedAt;
        private long completedAt;

        internal PlayDataTelemetry(EventBus events)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            subscription = events.Subscribe<PlayDataLoadStageEvent>(Observe);
        }

        internal void Start()
        {
            profiler = new Profiler<PlayDataLoadStage>();
            startedAt = Stopwatch.GetTimestamp();
            completedAt = 0L;
        }

        internal void Abort()
        {
            profiler = new Profiler<PlayDataLoadStage>();
            startedAt = 0L;
            completedAt = 0L;
        }

        internal void Complete()
        {
            if (completedAt == 0L)
            {
                completedAt = Stopwatch.GetTimestamp();
            }
        }

        internal LoadingMeasurement GetMeasurement()
        {
            long end = completedAt == 0L
                ? Stopwatch.GetTimestamp()
                : completedAt;
            double observed = startedAt == 0L
                ? 0.0
                : ToMilliseconds(Math.Max(0L, end - startedAt));
            List<LoadingStepMeasurement> steps = profiler
                .Snapshot()
                .OrderBy(item => item.Key)
                .Select(CreateMeasurement)
                .ToList();
            return new LoadingMeasurement(observed, steps);
        }

        public void Dispose()
        {
            subscription.Dispose();
        }

        private void Observe(PlayDataLoadStageEvent stageEvent)
        {
            if (stageEvent.Kind == PlayDataLoadStageEventKind.Completed ||
                stageEvent.Kind == PlayDataLoadStageEventKind.Failed)
            {
                profiler.Observe(
                    stageEvent.Stage,
                    stageEvent.Elapsed,
                    stageEvent.Kind == PlayDataLoadStageEventKind.Completed);
            }
        }

        private static LoadingStepMeasurement CreateMeasurement(
            ProfileMeasurement<PlayDataLoadStage> measurement)
        {
            double milliseconds = measurement.TotalTime.TotalMilliseconds;
            bool mainThread =
                measurement.Key >= PlayDataLoadStage.DeferredMainThreadWork;
            return new LoadingStepMeasurement(
                GetStep(measurement.Key),
                PlayDataLoadStageCatalog.GetReportStage(measurement.Key),
                PlayDataLoadStageCatalog.GetName(measurement.Key),
                measurement.Calls,
                milliseconds,
                milliseconds,
                mainThread ? milliseconds : 0.0,
                mainThread ? 0.0 : milliseconds,
                mainThread ? milliseconds : 0.0,
                mainThread ? 0.0 : milliseconds);
        }

        private static LoadingStep GetStep(PlayDataLoadStage stage)
        {
            switch (stage)
            {
                case PlayDataLoadStage.Reset:
                    return LoadingStep.ResetPlayData;
                case PlayDataLoadStage.InitializeMods:
                    return LoadingStep.InitializeMods;
                case PlayDataLoadStage.IndexModContent:
                    return LoadingStep.IndexModContent;
                case PlayDataLoadStage.PrepareModContent:
                    return LoadingStep.PrepareModContent;
                case PlayDataLoadStage.CreateModClasses:
                    return LoadingStep.CreateModClasses;
                case PlayDataLoadStage.LoadAndPatchXml:
                    return LoadingStep.LoadAndPatchXml;
                case PlayDataLoadStage.ImportDefinitions:
                    return LoadingStep.ImportDefinitions;
                case PlayDataLoadStage.EarlyBinding:
                    return LoadingStep.EarlyBinding;
                case PlayDataLoadStage.PreResolveImpliedDefinitions:
                    return LoadingStep.PreResolveImpliedDefinitions;
                case PlayDataLoadStage.CrossReferenceResolution:
                    return LoadingStep.CrossReferenceResolution;
                case PlayDataLoadStage.ReferenceResolution:
                    return LoadingStep.ReferenceResolution;
                case PlayDataLoadStage.PostResolveImpliedDefinitions:
                    return LoadingStep.PostResolveImpliedDefinitions;
                case PlayDataLoadStage.DefinitionFinalization:
                    return LoadingStep.DefinitionFinalization;
                case PlayDataLoadStage.InitializeRuntime:
                    return LoadingStep.InitializeRuntime;
                case PlayDataLoadStage.DeferredMainThreadWork:
                    return LoadingStep.DeferredMainThreadWork;
                case PlayDataLoadStage.Complete:
                    return LoadingStep.CompletePlayData;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
