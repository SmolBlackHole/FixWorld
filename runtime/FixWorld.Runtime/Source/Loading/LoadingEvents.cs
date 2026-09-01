using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FixWorld.Runtime;
using Verse;

namespace FixWorld.Loading
{
    internal enum LoadingStageEventKind
    {
        Started,
        Progress,
        Completed,
        Failed,
        Detail
    }

    internal readonly struct LoadingStageEvent
    {
        internal readonly long OperationId;
        internal readonly LoadingStageEventKind Kind;
        internal readonly LoadingStageEventSource Source;
        internal readonly LoadingStage Stage;
        internal readonly LoadingStep Operation;
        internal readonly string DisplayName;
        internal readonly string Activity;
        internal readonly LoadingModAttribution Attribution;
        internal readonly bool MainThread;
        internal readonly long ElapsedTicks;
        internal readonly bool RecordModTime;

        internal LoadingStageEvent(
            long operationId,
            LoadingStageEventKind kind,
            LoadingStageEventSource source,
            LoadingStage stage,
            LoadingStep operation,
            string displayName,
            string activity,
            LoadingModAttribution attribution,
            bool mainThread,
            long elapsedTicks,
            bool recordModTime = true)
        {
            OperationId = operationId;
            Kind = kind;
            Source = source;
            Stage = stage;
            Operation = operation;
            DisplayName = displayName;
            Activity = activity;
            Attribution = attribution;
            MainThread = mainThread;
            ElapsedTicks = elapsedTicks;
            RecordModTime = recordModTime;
        }
    }

    internal readonly struct LoadingStageEventDescriptor
    {
        internal readonly LoadingStage Stage;
        internal readonly LoadingStep Operation;
        internal readonly string DisplayName;
        internal readonly string Activity;
        internal readonly LoadingModAttribution Attribution;
        internal readonly LoadingStageEventSource Source;
        internal readonly bool RecordModTime;

        internal LoadingStageEventDescriptor(
            LoadingStage stage,
            LoadingStep operation,
            string displayName,
            string activity,
            LoadingModAttribution attribution,
            LoadingStageEventSource source = LoadingStageEventSource.FixWorld,
            bool recordModTime = true)
        {
            Stage = stage;
            Operation = operation;
            DisplayName = displayName;
            Activity = activity;
            Attribution = attribution;
            Source = source;
            RecordModTime = recordModTime;
        }
    }

    internal static class LoadingEvents
    {
        internal const string ProgressEventKey = "loading/progress";
        private const string DetailEventKey = "loading/detail";

        private static long nextOperationId;
        [ThreadStatic]
        private static List<ActiveOperation> activeOperations;

        internal static LoadingOperation Begin(LoadingStageEventDescriptor descriptor)
        {
            long operationId = Interlocked.Increment(ref nextOperationId);
            LoadingOperation operation = new LoadingOperation(
                operationId,
                descriptor,
                Stopwatch.GetTimestamp(),
                UnityData.IsInMainThread);
            FixWorldEvents.Publish(
                operation.CreateEvent(LoadingStageEventKind.Started, 0L));
            TrackOperation(operationId, descriptor.Operation);
            return operation;
        }

        internal static bool IsOperationActive(LoadingStep operation)
        {
            if (activeOperations == null)
            {
                return false;
            }

            for (int index = activeOperations.Count - 1; index >= 0; index--)
            {
                if (activeOperations[index].Operation == operation)
                {
                    return true;
                }
            }

            return false;
        }

        internal static LoadingOperation Begin(LoadingPipelineStage stage)
        {
            return Begin(new LoadingStageEventDescriptor(
                stage.Phase,
                stage.Operation,
                stage.Name,
                "Executing " + stage.TaskCount + " stage tasks   " +
                stage.ExecutionMode,
                LoadingModAttribution.Global,
                LoadingStageEventSource.FixWorld,
                recordModTime: false));
        }

        internal static void ReportWork(
            LoadingWorkItem item,
            int currentAction,
            int totalActions)
        {
            FixWorldEvents.PublishLatest(ProgressEventKey, new LoadingStageEvent(
                0L,
                LoadingStageEventKind.Progress,
                LoadingStageEventSource.FixWorld,
                item.Stage,
                item.Operation,
                item.DisplayName,
                item.Activity ?? "Delayed initialization task " + currentAction +
                " / " + totalActions,
                item.Attribution,
                true,
                0L));
        }

        internal static void ReportProfilerStep(
            StepDescriptor descriptor,
            bool mainThread)
        {
            string activity = descriptor.ModName == null
                ? null
                : descriptor.ModActivity + " for " + descriptor.ModName;
            FixWorldEvents.PublishLatest(ProgressEventKey, new LoadingStageEvent(
                0L,
                LoadingStageEventKind.Progress,
                LoadingStageEventSource.RimWorld,
                descriptor.Stage,
                descriptor.Step,
                descriptor.DisplayName,
                activity,
                LoadingModAttribution.Global,
                mainThread,
                0L));
        }

        internal static void ReportStageFallback(
            LoadingStage stage,
            bool mainThread)
        {
            FixWorldEvents.PublishLatest(ProgressEventKey, new LoadingStageEvent(
                0L,
                LoadingStageEventKind.Progress,
                LoadingStageEventSource.RimWorld,
                stage,
                default,
                LoadingStageNames.GetFallback(stage),
                null,
                LoadingModAttribution.Global,
                mainThread,
                0L));
        }

        internal static void ReportProfilerDetail(
            string label,
            bool mainThread)
        {
            FixWorldEvents.PublishLatest(
                DetailEventKey,
                new LoadingStageEvent(
                    0L,
                    LoadingStageEventKind.Detail,
                    LoadingStageEventSource.RimWorld,
                    default,
                    default,
                    label,
                    null,
                    LoadingModAttribution.Global,
                    mainThread,
                    0L));
        }

        internal static void CompleteOperation(long operationId)
        {
            if (activeOperations == null)
            {
                return;
            }

            for (int index = activeOperations.Count - 1; index >= 0; index--)
            {
                if (activeOperations[index].Id != operationId)
                {
                    continue;
                }

                activeOperations.RemoveAt(index);
                return;
            }
        }

        private static void TrackOperation(
            long operationId,
            LoadingStep operation)
        {
            if (activeOperations == null)
            {
                activeOperations = new List<ActiveOperation>(8);
            }

            activeOperations.Add(new ActiveOperation(operationId, operation));
        }

        private readonly struct ActiveOperation
        {
            internal readonly long Id;
            internal readonly LoadingStep Operation;

            internal ActiveOperation(long id, LoadingStep operation)
            {
                Id = id;
                Operation = operation;
            }
        }

    }

    internal sealed class LoadingOperation : IDisposable
    {
        private static readonly long ProgressIntervalTicks =
            Math.Max(1L, Stopwatch.Frequency * 150L / 1000L);
        private readonly long operationId;
        private readonly LoadingStageEventDescriptor descriptor;
        private readonly long startedAt;
        private readonly bool mainThread;
        private int completed;
        private long nextProgressAt;

        internal LoadingOperation(
            long operationId,
            LoadingStageEventDescriptor descriptor,
            long startedAt,
            bool mainThread)
        {
            this.operationId = operationId;
            this.descriptor = descriptor;
            this.startedAt = startedAt;
            this.mainThread = mainThread;
        }

        internal void ReportProgress(string activity = null, bool force = false)
        {
            if (Volatile.Read(ref completed) != 0)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            long next = Interlocked.Read(ref nextProgressAt);
            if (!force && now < next)
            {
                return;
            }

            Interlocked.Exchange(ref nextProgressAt, now + ProgressIntervalTicks);

            FixWorldEvents.PublishLatest(LoadingEvents.ProgressEventKey, new LoadingStageEvent(
                operationId,
                LoadingStageEventKind.Progress,
                descriptor.Source,
                descriptor.Stage,
                descriptor.Operation,
                descriptor.DisplayName,
                activity ?? descriptor.Activity,
                descriptor.Attribution,
                mainThread,
                now - startedAt));
        }

        internal void Fail()
        {
            Finish(LoadingStageEventKind.Failed);
        }

        public void Dispose()
        {
            Finish(LoadingStageEventKind.Completed);
        }

        internal LoadingStageEvent CreateEvent(
            LoadingStageEventKind kind,
            long elapsedTicks)
        {
            return new LoadingStageEvent(
                operationId,
                kind,
                descriptor.Source,
                descriptor.Stage,
                descriptor.Operation,
                descriptor.DisplayName,
                descriptor.Activity,
                descriptor.Attribution,
                mainThread,
                elapsedTicks,
                descriptor.RecordModTime);
        }

        private void Finish(LoadingStageEventKind kind)
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
            {
                return;
            }

            LoadingEvents.CompleteOperation(operationId);
            FixWorldEvents.Publish(CreateEvent(
                kind,
                Stopwatch.GetTimestamp() - startedAt));
        }
    }
}
