using System;
using System.Diagnostics;

namespace FixWorld.Scheduling
{
    internal enum SchedulerEventKind
    {
        Queued,
        Started,
        Completed,
        Failed,
        Cancelled
    }

    internal enum SchedulerEventSource
    {
        Worker,
        MainThread
    }

    internal readonly struct SchedulerEvent
    {
        internal readonly SchedulerEventKind Kind;
        internal readonly SchedulerEventSource Source;
        internal readonly string Key;
        internal readonly string Name;
        internal readonly SchedulerJobLifetime Lifetime;
        internal readonly SchedulerJobPriority Priority;
        internal readonly SchedulerResourceClass ResourceClass;
        internal readonly SchedulerJobState State;
        internal readonly long WaitTicks;
        internal readonly long ExecutionTicks;
        internal readonly long WallTicks;
        internal readonly Exception Exception;

        internal double WaitMilliseconds =>
            WaitTicks * 1000.0 / Stopwatch.Frequency;
        internal double ExecutionMilliseconds =>
            ExecutionTicks * 1000.0 / Stopwatch.Frequency;
        internal double WallMilliseconds =>
            WallTicks * 1000.0 / Stopwatch.Frequency;

        internal SchedulerEvent(
            SchedulerEventKind kind,
            SchedulerEventSource source,
            string key,
            string name,
            SchedulerJobLifetime lifetime,
            SchedulerJobPriority priority,
            SchedulerResourceClass resourceClass,
            SchedulerJobState state,
            long waitTicks,
            long executionTicks,
            long wallTicks,
            Exception exception)
        {
            Kind = kind;
            Source = source;
            Key = key;
            Name = name;
            Lifetime = lifetime;
            Priority = priority;
            ResourceClass = resourceClass;
            State = state;
            WaitTicks = waitTicks;
            ExecutionTicks = executionTicks;
            WallTicks = wallTicks;
            Exception = exception;
        }
    }

    internal static class SchedulerMailbox
    {
        private const int MaximumEventsPerDrain = 2048;
        private static readonly EventMailbox<SchedulerEvent> Events =
            new EventMailbox<SchedulerEvent>(MaximumEventsPerDrain);

        internal static IDisposable Subscribe(Action<SchedulerEvent> subscriber)
        {
            return Events.Subscribe(subscriber);
        }

        internal static void Publish(ScheduledJobHandle handle, SchedulerEventKind kind)
        {
            Events.Publish(new SchedulerEvent(
                kind,
                SchedulerEventSource.Worker,
                handle.Key,
                handle.Name,
                handle.Lifetime,
                handle.Priority,
                handle.ResourceClass,
                handle.State,
                handle.WaitTicks,
                handle.ExecutionTicks,
                handle.WallTicks,
                handle.Exception));
        }

        internal static void Publish(MainThreadActionHandle handle, SchedulerEventKind kind)
        {
            Events.Publish(new SchedulerEvent(
                kind,
                SchedulerEventSource.MainThread,
                handle.Key,
                handle.Name,
                SchedulerJobLifetime.Critical,
                SchedulerJobPriority.High,
                SchedulerResourceClass.Cpu,
                handle.State,
                handle.WaitTicks,
                handle.ExecutionTicks,
                Math.Max(0L, Stopwatch.GetTimestamp() - handle.QueuedAt),
                handle.Exception));
        }

    }
}
