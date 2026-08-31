using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace FixWorld.Scheduling
{
    internal static class FixWorldScheduler
    {
        private static readonly object Sync = new object();
        private static SchedulerRuntime runtime;

        internal static int WorkerCount => GetRuntime().WorkerCount;

        internal static void Initialize()
        {
            GetRuntime();
        }

        internal static ScheduledJobHandle<TResult> Schedule<TResult>(
            SchedulerJob<TResult> job)
        {
            return GetRuntime().Schedule(job);
        }

        internal static MainThreadActionHandle Dispatch(
            string key,
            string name,
            Action action)
        {
            return GetRuntime().Dispatch(key, name, action);
        }

        internal static void BindMainThread()
        {
            GetRuntime().BindMainThread();
        }

        internal static int PumpMainThread(
            int maximumActions = 64,
            int maximumMilliseconds = 4)
        {
            SchedulerRuntime current = Volatile.Read(ref runtime);
            if (current == null)
            {
                return 0;
            }

            int executed = current.PumpMainThread(
                maximumActions,
                maximumMilliseconds);
            EventMailboxPump.DrainAll();
            return executed;
        }

        internal static int DrainEvents()
        {
            return EventMailboxPump.DrainAll();
        }

        internal static void Cancel(ScheduledJobHandle handle)
        {
            SchedulerRuntime current = Volatile.Read(ref runtime);
            current?.Cancel(handle);
        }

        internal static void Shutdown()
        {
            SchedulerRuntime current;
            lock (Sync)
            {
                current = runtime;
                runtime = null;
            }

            current?.Dispose();
        }

        private static SchedulerRuntime GetRuntime()
        {
            SchedulerRuntime current = Volatile.Read(ref runtime);
            if (current != null)
            {
                return current;
            }

            lock (Sync)
            {
                if (runtime == null)
                {
                    runtime = SchedulerRuntime.CreateDefault();
                }

                return runtime;
            }
        }
    }

    internal sealed class SchedulerRuntime : IDisposable
    {
        private const string WorkerEnvironmentVariable = "FIXWORLD_WORKERS";
        private const string IoEnvironmentVariable = "FIXWORLD_SCHEDULER_IO";
        private const string QueueEnvironmentVariable = "FIXWORLD_SCHEDULER_QUEUE";
        private const string ByteEnvironmentVariable = "FIXWORLD_SCHEDULER_BYTES";
        private const int DefaultQueueCapacity = 4096;
        private const long DefaultByteCapacity = 512L * 1024L * 1024L;

        private readonly object sync = new object();
        private readonly List<ScheduledJobHandle> pending =
            new List<ScheduledJobHandle>();
        private readonly Dictionary<string, ScheduledJobHandle> jobsByKey =
            new Dictionary<string, ScheduledJobHandle>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> runningByConcurrencyKey =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Thread[] workers;
        private readonly CancellationTokenSource shutdown =
            new CancellationTokenSource();
        private readonly MainThreadDispatcher dispatcher;
        private readonly int ioLimit;
        private readonly int queueCapacity;
        private readonly long byteCapacity;

        private long nextSequence;
        private long activeBytes;
        private int activeJobs;
        private int activeIoJobs;
        private bool disposed;

        internal int WorkerCount => workers.Length;

        private SchedulerRuntime(
            int workerCount,
            int ioLimit,
            int queueCapacity,
            long byteCapacity)
        {
            this.ioLimit = ioLimit;
            this.queueCapacity = queueCapacity;
            this.byteCapacity = byteCapacity;
            dispatcher = new MainThreadDispatcher(queueCapacity);
            workers = new Thread[workerCount];
            for (int index = 0; index < workers.Length; index++)
            {
                Thread worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "FixWorld Worker " + (index + 1).ToString(CultureInfo.InvariantCulture)
                };
                workers[index] = worker;
                worker.Start();
            }

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        internal static SchedulerRuntime CreateDefault()
        {
            int defaultWorkers = Math.Max(1, Environment.ProcessorCount / 2);
            int workerCount = ReadBoundedInt(
                WorkerEnvironmentVariable,
                defaultWorkers,
                1,
                Math.Max(1, Environment.ProcessorCount));
            int ioLimit = ReadBoundedInt(
                IoEnvironmentVariable,
                Math.Max(1, workerCount / 2),
                1,
                workerCount);
            int queueCapacity = ReadBoundedInt(
                QueueEnvironmentVariable,
                DefaultQueueCapacity,
                workerCount,
                65536);
            long byteCapacity = ReadBoundedLong(
                ByteEnvironmentVariable,
                DefaultByteCapacity,
                16L * 1024L * 1024L,
                64L * 1024L * 1024L * 1024L);
            return new SchedulerRuntime(
                workerCount,
                ioLimit,
                queueCapacity,
                byteCapacity);
        }

        internal ScheduledJobHandle<TResult> Schedule<TResult>(
            SchedulerJob<TResult> job)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            lock (sync)
            {
                ThrowIfDisposed();
                if (jobsByKey.TryGetValue(job.Key, out ScheduledJobHandle existing))
                {
                    if (existing is ScheduledJobHandle<TResult> typed)
                    {
                        return typed;
                    }

                    throw new InvalidOperationException(
                        "Scheduler job key is already used with result type " +
                        existing.ResultType.FullName + ": " + job.Key);
                }

                if (pending.Count >= queueCapacity)
                {
                    throw new InvalidOperationException(
                        "FixWorld scheduler queue is full (" + queueCapacity + ").");
                }

                ScheduledJobHandle<TResult> handle =
                    new ScheduledJobHandle<TResult>(++nextSequence, job);
                jobsByKey.Add(job.Key, handle);
                pending.Add(handle);
                SchedulerMailbox.Publish(handle, SchedulerEventKind.Queued);
                Monitor.PulseAll(sync);
                return handle;
            }
        }

        internal MainThreadActionHandle Dispatch(
            string key,
            string name,
            Action action)
        {
            lock (sync)
            {
                ThrowIfDisposed();
            }

            return dispatcher.Enqueue(key, name, action);
        }

        internal int PumpMainThread(int maximumActions, int maximumMilliseconds)
        {
            return dispatcher.Pump(maximumActions, maximumMilliseconds);
        }

        internal void BindMainThread()
        {
            dispatcher.BindCurrentThread();
        }

        internal void Cancel(ScheduledJobHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            handle.Cancel();
            lock (sync)
            {
                Monitor.PulseAll(sync);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                shutdown.Cancel();
                foreach (ScheduledJobHandle handle in jobsByKey.Values)
                {
                    handle.Cancel();
                }

                foreach (ScheduledJobHandle handle in pending)
                {
                    if (handle.State == SchedulerJobState.Cancelled)
                    {
                        SchedulerMailbox.Publish(handle, SchedulerEventKind.Cancelled);
                    }
                }

                pending.Clear();
                Monitor.PulseAll(sync);
            }

            dispatcher.CancelAll();
            long deadline = Stopwatch.GetTimestamp() + 2L * Stopwatch.Frequency;
            foreach (Thread worker in workers)
            {
                long remainingTicks = deadline - Stopwatch.GetTimestamp();
                int remaining = (int)Math.Max(
                    0L,
                    Math.Min(
                        int.MaxValue,
                        remainingTicks * 1000L / Stopwatch.Frequency));
                if (remaining == 0)
                {
                    break;
                }

                worker.Join(remaining);
            }

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            shutdown.Dispose();
        }

        private void WorkerLoop()
        {
            while (true)
            {
                ScheduledJobHandle handle;
                lock (sync)
                {
                    while (true)
                    {
                        if (disposed)
                        {
                            return;
                        }

                        handle = TakeNextReadyJob();
                        if (handle != null)
                        {
                            Reserve(handle);
                            break;
                        }

                        Monitor.Wait(sync, 250);
                    }
                }

                if (!handle.TryStart())
                {
                    lock (sync)
                    {
                        Release(handle);
                        Monitor.PulseAll(sync);
                    }

                    continue;
                }

                SchedulerMailbox.Publish(handle, SchedulerEventKind.Started);
                handle.Run(shutdown.Token);
                SchedulerMailbox.Publish(handle, GetTerminalEvent(handle.State));

                lock (sync)
                {
                    Release(handle);
                    Monitor.PulseAll(sync);
                }
            }
        }

        private ScheduledJobHandle TakeNextReadyJob()
        {
            ScheduledJobHandle best = null;
            int bestIndex = -1;
            for (int index = pending.Count - 1; index >= 0; index--)
            {
                ScheduledJobHandle candidate = pending[index];
                if (candidate.IsTerminal)
                {
                    pending.RemoveAt(index);
                    continue;
                }

                if (!DependenciesReady(candidate, out Exception dependencyError))
                {
                    if (dependencyError != null)
                    {
                        candidate.CancelBecauseDependencyFailed(dependencyError);
                        pending.RemoveAt(index);
                        SchedulerMailbox.Publish(
                            candidate,
                            SchedulerEventKind.Cancelled);
                    }

                    continue;
                }

                if (!ResourcesAvailable(candidate))
                {
                    continue;
                }

                if (best == null || Compare(candidate, best) < 0)
                {
                    best = candidate;
                    bestIndex = index;
                }
            }

            if (bestIndex >= 0)
            {
                pending.RemoveAt(bestIndex);
            }

            return best;
        }

        private static bool DependenciesReady(
            ScheduledJobHandle handle,
            out Exception dependencyError)
        {
            dependencyError = null;
            foreach (ScheduledJobHandle dependency in handle.Dependencies)
            {
                SchedulerJobState state = dependency.State;
                if (state == SchedulerJobState.Failed ||
                    state == SchedulerJobState.Cancelled)
                {
                    dependencyError = dependency.Exception ??
                        new InvalidOperationException(
                            "Scheduler dependency failed: " + dependency.Key);
                    return false;
                }

                if (state != SchedulerJobState.Completed)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ResourcesAvailable(ScheduledJobHandle handle)
        {
            if ((handle.ResourceClass == SchedulerResourceClass.Io ||
                 handle.ResourceClass == SchedulerResourceClass.Mixed) &&
                activeIoJobs >= ioLimit)
            {
                return false;
            }

            if (handle.EstimatedBytes > 0L &&
                activeJobs > 0 &&
                activeBytes + handle.EstimatedBytes > byteCapacity)
            {
                return false;
            }

            if (handle.MaxConcurrency <= 0)
            {
                return true;
            }

            return !runningByConcurrencyKey.TryGetValue(
                       handle.ConcurrencyKey,
                       out int running) ||
                   running < handle.MaxConcurrency;
        }

        private void Reserve(ScheduledJobHandle handle)
        {
            activeJobs++;
            activeBytes += handle.EstimatedBytes;
            if (handle.ResourceClass == SchedulerResourceClass.Io ||
                handle.ResourceClass == SchedulerResourceClass.Mixed)
            {
                activeIoJobs++;
            }

            if (handle.MaxConcurrency > 0)
            {
                runningByConcurrencyKey.TryGetValue(
                    handle.ConcurrencyKey,
                    out int running);
                runningByConcurrencyKey[handle.ConcurrencyKey] = running + 1;
            }
        }

        private void Release(ScheduledJobHandle handle)
        {
            activeJobs = Math.Max(0, activeJobs - 1);
            activeBytes = Math.Max(0L, activeBytes - handle.EstimatedBytes);
            if (handle.ResourceClass == SchedulerResourceClass.Io ||
                handle.ResourceClass == SchedulerResourceClass.Mixed)
            {
                activeIoJobs = Math.Max(0, activeIoJobs - 1);
            }

            if (handle.MaxConcurrency <= 0 ||
                !runningByConcurrencyKey.TryGetValue(
                    handle.ConcurrencyKey,
                    out int running))
            {
                return;
            }

            if (running <= 1)
            {
                runningByConcurrencyKey.Remove(handle.ConcurrencyKey);
            }
            else
            {
                runningByConcurrencyKey[handle.ConcurrencyKey] = running - 1;
            }
        }

        private static int Compare(ScheduledJobHandle left, ScheduledJobHandle right)
        {
            int lifetime = left.Lifetime.CompareTo(right.Lifetime);
            if (lifetime != 0)
            {
                return lifetime;
            }

            int priority = left.Priority.CompareTo(right.Priority);
            return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
        }

        private static SchedulerEventKind GetTerminalEvent(SchedulerJobState state)
        {
            switch (state)
            {
                case SchedulerJobState.Completed:
                    return SchedulerEventKind.Completed;
                case SchedulerJobState.Failed:
                    return SchedulerEventKind.Failed;
                default:
                    return SchedulerEventKind.Cancelled;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SchedulerRuntime));
            }
        }

        private void OnProcessExit(object sender, EventArgs eventArgs)
        {
            Dispose();
        }

        private static int ReadBoundedInt(
            string name,
            int fallback,
            int minimum,
            int maximum)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return int.TryParse(
                       value,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out int parsed)
                ? Math.Max(minimum, Math.Min(maximum, parsed))
                : fallback;
        }

        private static long ReadBoundedLong(
            string name,
            long fallback,
            long minimum,
            long maximum)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return long.TryParse(
                       value,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out long parsed)
                ? Math.Max(minimum, Math.Min(maximum, parsed))
                : fallback;
        }
    }

    internal sealed class MainThreadDispatcher
    {
        private readonly ConcurrentQueue<MainThreadActionHandle> actions =
            new ConcurrentQueue<MainThreadActionHandle>();
        private readonly int capacity;
        private int mainThreadId;
        private int queued;
        private bool cancelled;

        internal MainThreadDispatcher(int capacity)
        {
            this.capacity = capacity;
        }

        internal void BindCurrentThread()
        {
            int current = Thread.CurrentThread.ManagedThreadId;
            int existing = Interlocked.CompareExchange(
                ref mainThreadId,
                current,
                0);
            if (existing != 0 && existing != current)
            {
                throw new InvalidOperationException(
                    "FixWorld main-thread dispatcher was bound to another thread.");
            }
        }

        internal MainThreadActionHandle Enqueue(
            string key,
            string name,
            Action action)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A main-thread action needs a stable key.",
                    nameof(key));
            }

            if (Volatile.Read(ref cancelled))
            {
                throw new ObjectDisposedException(nameof(MainThreadDispatcher));
            }

            int count = Interlocked.Increment(ref queued);
            if (count > capacity)
            {
                Interlocked.Decrement(ref queued);
                throw new InvalidOperationException(
                    "FixWorld main-thread dispatcher is full (" + capacity + ").");
            }

            MainThreadActionHandle handle =
                new MainThreadActionHandle(key, name, action);
            actions.Enqueue(handle);
            SchedulerMailbox.Publish(handle, SchedulerEventKind.Queued);
            return handle;
        }

        internal int Pump(int maximumActions, int maximumMilliseconds)
        {
            int boundThread = Volatile.Read(ref mainThreadId);
            if (boundThread == 0 ||
                Thread.CurrentThread.ManagedThreadId != boundThread)
            {
                return 0;
            }

            int actionLimit = Math.Max(1, maximumActions);
            long deadline = Stopwatch.GetTimestamp() + Math.Max(
                1L,
                Stopwatch.Frequency * Math.Max(1, maximumMilliseconds) / 1000L);
            int executed = 0;
            while (executed < actionLimit &&
                   Stopwatch.GetTimestamp() <= deadline &&
                   actions.TryDequeue(out MainThreadActionHandle handle))
            {
                Interlocked.Decrement(ref queued);
                SchedulerMailbox.Publish(handle, SchedulerEventKind.Started);
                handle.Run();
                SchedulerMailbox.Publish(
                    handle,
                    handle.State == SchedulerJobState.Completed
                        ? SchedulerEventKind.Completed
                        : SchedulerEventKind.Failed);
                executed++;
            }

            return executed;
        }

        internal void CancelAll()
        {
            Volatile.Write(ref cancelled, true);
            while (actions.TryDequeue(out MainThreadActionHandle handle))
            {
                Interlocked.Decrement(ref queued);
                handle.Cancel();
                SchedulerMailbox.Publish(handle, SchedulerEventKind.Cancelled);
            }
        }
    }
}
