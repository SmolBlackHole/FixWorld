using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace FixWorld.Scheduling
{
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
        private bool shutdownDisposed;

        internal int WorkerCount => workers.Length;

        private SchedulerRuntime(
            int workerCount,
            int ioLimit,
            int queueCapacity,
            long byteCapacity,
            Action<string, Exception> mainThreadErrorHandler)
        {
            this.ioLimit = ioLimit;
            this.queueCapacity = queueCapacity;
            this.byteCapacity = byteCapacity;
            dispatcher = new MainThreadDispatcher(
                queueCapacity,
                mainThreadErrorHandler ?? ReportMainThreadError);
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

        internal static SchedulerRuntime CreateDefault(
            Action<string, Exception> mainThreadErrorHandler = null)
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
                byteCapacity,
                mainThreadErrorHandler);
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
                    if (existing.IsTerminal)
                    {
                        jobsByKey.Remove(job.Key);
                    }
                    else if (existing is ScheduledJobHandle<TResult> typed)
                    {
                        return typed;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "Scheduler job key is already used with result type " +
                            existing.ResultType.FullName + ": " + job.Key);
                    }
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
                Monitor.PulseAll(sync);
                return handle;
            }
        }

        internal void Post(
            string name,
            Action action)
        {
            lock (sync)
            {
                ThrowIfDisposed();
            }

            dispatcher.Post(name, action);
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
                RemoveTerminalJob(handle);
                Monitor.PulseAll(sync);
            }
        }

        public void Dispose()
        {
            Shutdown(2000);
        }

        internal bool Shutdown(int maximumMilliseconds)
        {
            lock (sync)
            {
                if (!disposed)
                {
                    disposed = true;
                    shutdown.Cancel();
                    foreach (ScheduledJobHandle handle in jobsByKey.Values)
                    {
                        handle.Cancel();
                    }

                    pending.Clear();
                    jobsByKey.Clear();
                    Monitor.PulseAll(sync);
                    dispatcher.CancelAll();
                    AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                }
            }

            long timeout = Math.Max(0, maximumMilliseconds);
            long deadline = Stopwatch.GetTimestamp() +
                            timeout * Stopwatch.Frequency / 1000L;
            bool workersStopped = true;
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
                    workersStopped = false;
                    break;
                }

                if (!worker.Join(remaining))
                {
                    workersStopped = false;
                }
            }

            if (workersStopped)
            {
                lock (sync)
                {
                    if (!shutdownDisposed)
                    {
                        shutdownDisposed = true;
                        shutdown.Dispose();
                    }
                }
            }

            return workersStopped;
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
                        RemoveTerminalJob(handle);
                        Monitor.PulseAll(sync);
                    }

                    continue;
                }

                handle.Run(shutdown.Token);

                lock (sync)
                {
                    Release(handle);
                    RemoveTerminalJob(handle);
                    Monitor.PulseAll(sync);
                }
            }
        }

        private ScheduledJobHandle TakeNextReadyJob()
        {
            for (int index = pending.Count - 1; index >= 0; index--)
            {
                ScheduledJobHandle candidate = pending[index];
                if (candidate.IsTerminal)
                {
                    pending.RemoveAt(index);
                    RemoveTerminalJob(candidate);
                    continue;
                }

                DependenciesReady(candidate, out Exception dependencyError);
                if (dependencyError == null)
                {
                    continue;
                }

                candidate.CancelBecauseDependencyFailed(dependencyError);
                pending.RemoveAt(index);
                RemoveTerminalJob(candidate);
            }

            ScheduledJobHandle best = null;
            int bestIndex = -1;
            for (int index = 0; index < pending.Count; index++)
            {
                ScheduledJobHandle candidate = pending[index];
                if (!DependenciesReady(candidate, out _))
                {
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

        private void RemoveTerminalJob(ScheduledJobHandle handle)
        {
            if (!handle.IsTerminal)
            {
                return;
            }

            if (jobsByKey.TryGetValue(handle.Key, out ScheduledJobHandle current) &&
                ReferenceEquals(current, handle))
            {
                jobsByKey.Remove(handle.Key);
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
            Shutdown(2000);
        }

        private static void ReportMainThreadError(
            string name,
            Exception exception)
        {
            Console.Error.WriteLine(
                "FixWorld main-thread action failed (" + name + "): " +
                exception);
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

}
