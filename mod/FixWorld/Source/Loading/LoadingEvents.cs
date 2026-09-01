using System;
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
        internal readonly string Subject;
        internal readonly LoadingModAttribution Attribution;
        internal readonly LoadingThreadAffinity Affinity;
        internal readonly bool MainThread;
        internal readonly int Current;
        internal readonly int Total;
        internal readonly long ElapsedTicks;

        internal LoadingStageEvent(
            long operationId,
            LoadingStageEventKind kind,
            LoadingStageEventSource source,
            LoadingStage stage,
            LoadingStep operation,
            string displayName,
            string activity,
            string subject,
            LoadingModAttribution attribution,
            LoadingThreadAffinity affinity,
            bool mainThread,
            int current,
            int total,
            long elapsedTicks)
        {
            OperationId = operationId;
            Kind = kind;
            Source = source;
            Stage = stage;
            Operation = operation;
            DisplayName = displayName;
            Activity = activity;
            Subject = subject;
            Attribution = attribution;
            Affinity = affinity;
            MainThread = mainThread;
            Current = current;
            Total = total;
            ElapsedTicks = elapsedTicks;
        }
    }

    internal readonly struct LoadingStageEventDescriptor
    {
        internal readonly LoadingStage Stage;
        internal readonly LoadingStep Operation;
        internal readonly string DisplayName;
        internal readonly string Activity;
        internal readonly string Subject;
        internal readonly LoadingModAttribution Attribution;
        internal readonly LoadingThreadAffinity Affinity;
        internal readonly LoadingStageEventSource Source;

        internal LoadingStageEventDescriptor(
            LoadingStage stage,
            LoadingStep operation,
            string displayName,
            string activity,
            string subject,
            LoadingModAttribution attribution,
            LoadingThreadAffinity affinity = LoadingThreadAffinity.MainThread,
            LoadingStageEventSource source = LoadingStageEventSource.FixWorld)
        {
            Stage = stage;
            Operation = operation;
            DisplayName = displayName;
            Activity = activity;
            Subject = subject;
            Attribution = attribution;
            Affinity = affinity;
            Source = source;
        }
    }

    internal static class LoadingEvents
    {
        internal const string ProgressEventKey = "loading/progress";
        private const string DetailEventKey = "loading/detail";

        private static long nextOperationId;

        internal static LoadingOperation Begin(LoadingStageEventDescriptor descriptor)
        {
            long operationId = Interlocked.Increment(ref nextOperationId);
            LoadingOperation operation = new LoadingOperation(
                operationId,
                descriptor,
                Stopwatch.GetTimestamp(),
                UnityData.IsInMainThread);
            FixWorldEvents.Publish(
                operation.CreateEvent(LoadingStageEventKind.Started, 0, 0, 0L));
            return operation;
        }

        internal static void ReportStage(
            LoadingPipelineStage stage,
            int completedTasks,
            int totalTasks)
        {
            FixWorldEvents.PublishLatest(
                ProgressEventKey,
                new LoadingStageEvent(
                0L,
                LoadingStageEventKind.Progress,
                LoadingStageEventSource.FixWorld,
                stage.Phase,
                stage.Operation,
                stage.Name,
                "Stage tasks " + completedTasks + " / " + totalTasks +
                "   " + stage.ExecutionMode,
                stage.Name,
                LoadingModAttribution.Global,
                LoadingThreadAffinity.MainThread,
                true,
                completedTasks,
                totalTasks,
                0L));
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
                item.Subject,
                item.Attribution,
                item.Affinity,
                true,
                currentAction,
                totalActions,
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
                descriptor.ModName,
                LoadingModAttribution.Global,
                LoadingThreadAffinity.MainThread,
                mainThread,
                0,
                0,
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
                null,
                LoadingModAttribution.Global,
                LoadingThreadAffinity.MainThread,
                mainThread,
                0,
                0,
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
                    null,
                    LoadingModAttribution.Global,
                    LoadingThreadAffinity.MainThread,
                    mainThread,
                    0,
                    0,
                    0L));
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

        internal void ReportProgress(int current, int total, string activity = null)
        {
            if (Volatile.Read(ref completed) != 0)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            long next = Interlocked.Read(ref nextProgressAt);
            if (current < total && now < next)
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
                descriptor.Subject,
                descriptor.Attribution,
                descriptor.Affinity,
                mainThread,
                current,
                total,
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
            int current,
            int total,
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
                descriptor.Subject,
                descriptor.Attribution,
                descriptor.Affinity,
                mainThread,
                current,
                total,
                elapsedTicks);
        }

        private void Finish(LoadingStageEventKind kind)
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
            {
                return;
            }

            FixWorldEvents.Publish(CreateEvent(
                kind,
                1,
                1,
                Stopwatch.GetTimestamp() - startedAt));
        }
    }
}
