using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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

    internal static class LoadingStageMailbox
    {
        private const int MaximumEventsPerDrain = 1024;
        private static readonly ConcurrentQueue<LoadingStageEvent> Events =
            new ConcurrentQueue<LoadingStageEvent>();
        private static readonly object SubscriberSync = new object();
        private static readonly object LatestSync = new object();
        private static readonly List<Action<LoadingStageEvent>> Subscribers =
            new List<Action<LoadingStageEvent>>();

        private static long nextOperationId;
        private static Action<LoadingStageEvent>[] subscriberSnapshot =
            Array.Empty<Action<LoadingStageEvent>>();
        private static bool hasLatest;
        private static LoadingStageEvent latest;
        private static bool hasLatestDetail;
        private static LoadingStageEvent latestDetail;

        internal static LoadingStageOperation Begin(LoadingStageEventDescriptor descriptor)
        {
            long operationId = Interlocked.Increment(ref nextOperationId);
            LoadingStageOperation operation = new LoadingStageOperation(
                operationId,
                descriptor,
                Stopwatch.GetTimestamp(),
                UnityData.IsInMainThread);
            Publish(operation.CreateEvent(LoadingStageEventKind.Started, 0, 0, 0L));
            return operation;
        }

        internal static IDisposable Subscribe(Action<LoadingStageEvent> subscriber)
        {
            if (subscriber == null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            lock (SubscriberSync)
            {
                Subscribers.Add(subscriber);
                subscriberSnapshot = Subscribers.ToArray();
            }

            return new Subscription(subscriber);
        }

        internal static void Publish(LoadingStageEvent stageEvent)
        {
            Events.Enqueue(stageEvent);
        }

        internal static void PublishLatest(LoadingStageEvent stageEvent)
        {
            lock (LatestSync)
            {
                latest = stageEvent;
                hasLatest = true;
            }
        }

        internal static void ReportStage(
            LoadingPipelineStage stage,
            int completedTasks,
            int totalTasks)
        {
            PublishLatest(new LoadingStageEvent(
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
            PublishLatest(new LoadingStageEvent(
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

        internal static void ReportProfilerStep(StepDescriptor descriptor)
        {
            string activity = descriptor.ModName == null
                ? null
                : descriptor.ModActivity + " for " + descriptor.ModName;
            PublishLatest(new LoadingStageEvent(
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
                true,
                0,
                0,
                0L));
        }

        internal static void ReportStageFallback(LoadingStage stage)
        {
            PublishLatest(new LoadingStageEvent(
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
                true,
                0,
                0,
                0L));
        }

        internal static void ReportProfilerDetail(string label)
        {
            lock (LatestSync)
            {
                latestDetail = new LoadingStageEvent(
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
                    true,
                    0,
                    0,
                    0L);
                hasLatestDetail = true;
            }
        }

        internal static int Drain()
        {
            if (!UnityData.IsInMainThread)
            {
                return 0;
            }

            Action<LoadingStageEvent>[] subscribers =
                Volatile.Read(ref subscriberSnapshot);

            int drained = 0;
            while (drained < MaximumEventsPerDrain && Events.TryDequeue(out LoadingStageEvent item))
            {
                Notify(subscribers, item);
                drained++;
            }

            LoadingStageEvent latestItem = default;
            bool deliverLatest;
            lock (LatestSync)
            {
                deliverLatest = hasLatest;
                if (deliverLatest)
                {
                    latestItem = latest;
                    hasLatest = false;
                }
            }

            if (deliverLatest)
            {
                Notify(subscribers, latestItem);
                drained++;
            }

            LoadingStageEvent detailItem = default;
            bool deliverDetail;
            lock (LatestSync)
            {
                deliverDetail = hasLatestDetail;
                if (deliverDetail)
                {
                    detailItem = latestDetail;
                    hasLatestDetail = false;
                }
            }

            if (deliverDetail)
            {
                Notify(subscribers, detailItem);
                drained++;
            }

            return drained;
        }

        private static void Notify(
            Action<LoadingStageEvent>[] subscribers,
            LoadingStageEvent item)
        {
            foreach (Action<LoadingStageEvent> subscriber in subscribers)
            {
                try
                {
                    subscriber(item);
                }
                catch (Exception exception)
                {
                    Log.Error("[FixWorld] Loading stage event hook failed: " + exception);
                }
            }
        }

        private static void Unsubscribe(Action<LoadingStageEvent> subscriber)
        {
            lock (SubscriberSync)
            {
                Subscribers.Remove(subscriber);
                subscriberSnapshot = Subscribers.ToArray();
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action<LoadingStageEvent> subscriber;

            internal Subscription(Action<LoadingStageEvent> subscriber)
            {
                this.subscriber = subscriber;
            }

            public void Dispose()
            {
                Action<LoadingStageEvent> current =
                    Interlocked.Exchange(ref subscriber, null);
                if (current != null)
                {
                    Unsubscribe(current);
                }
            }
        }
    }

    internal sealed class LoadingStageOperation : IDisposable
    {
        private static readonly long ProgressIntervalTicks =
            Math.Max(1L, Stopwatch.Frequency * 150L / 1000L);
        private readonly long operationId;
        private readonly LoadingStageEventDescriptor descriptor;
        private readonly long startedAt;
        private readonly bool mainThread;
        private int completed;
        private long nextProgressAt;

        internal LoadingStageOperation(
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

            LoadingStageMailbox.PublishLatest(new LoadingStageEvent(
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

            LoadingStageMailbox.Publish(CreateEvent(
                kind,
                1,
                1,
                Stopwatch.GetTimestamp() - startedAt));
        }
    }
}
