using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace FixWorld.Scheduling
{
    public sealed class JobScheduler : IDisposable
    {
        private readonly object sync = new object();
        private readonly List<JobHandle> pending = new List<JobHandle>();
        private readonly Dictionary<string, JobHandle> jobsByKey =
            new Dictionary<string, JobHandle>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> runningByConcurrencyKey =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly CancellationTokenSource shutdown =
            new CancellationTokenSource();
        private readonly JobSchedulerOptions options;
        private readonly Thread[] workers;

        private long nextSequence;
        private long activeBytes;
        private int activeJobs;
        private int activeIoJobs;
        private bool disposed;
        private bool shutdownDisposed;

        public JobScheduler(JobSchedulerOptions options)
        {
            this.options = options ??
                throw new ArgumentNullException(nameof(options));
            workers = new Thread[options.WorkerCount];
            for (int index = 0; index < workers.Length; index++)
            {
                Thread worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = options.WorkerNamePrefix + " " +
                           (index + 1).ToString(CultureInfo.InvariantCulture)
                };
                workers[index] = worker;
                worker.Start();
            }
        }

        public int WorkerCount => workers.Length;

        public JobHandle<TResult> Schedule<TResult>(Job<TResult> job)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            lock (sync)
            {
                ThrowIfDisposed();
                ValidateDependencies(job.Dependencies);
                if (jobsByKey.TryGetValue(job.Key, out JobHandle existing))
                {
                    if (existing.IsTerminal)
                    {
                        jobsByKey.Remove(job.Key);
                    }
                    else if (existing is JobHandle<TResult> typed)
                    {
                        return typed;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "Job key is already active with result type " +
                            existing.ResultType.FullName + ": " + job.Key);
                    }
                }

                if (pending.Count >= options.QueueCapacity)
                {
                    throw new InvalidOperationException(
                        "The job queue is full (" +
                        options.QueueCapacity + ").");
                }

                JobHandle<TResult> handle = new JobHandle<TResult>(
                    this,
                    ++nextSequence,
                    job);
                jobsByKey.Add(job.Key, handle);
                pending.Add(handle);
                Monitor.PulseAll(sync);
                return handle;
            }
        }

        public void Cancel(JobHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            if (!ReferenceEquals(handle.Owner, this))
            {
                throw new ArgumentException(
                    "The job belongs to a different scheduler.",
                    nameof(handle));
            }

            handle.Cancel();
            lock (sync)
            {
                RemoveTerminalJob(handle);
                Monitor.PulseAll(sync);
            }
        }

        public bool Shutdown(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            lock (sync)
            {
                if (!disposed)
                {
                    disposed = true;
                    shutdown.Cancel();
                    foreach (JobHandle handle in jobsByKey.Values)
                    {
                        handle.Cancel();
                    }

                    pending.Clear();
                    jobsByKey.Clear();
                    Monitor.PulseAll(sync);
                }
            }

            long timeoutTicks = (long)Math.Min(
                long.MaxValue,
                timeout.TotalSeconds * Stopwatch.Frequency);
            long deadline = Stopwatch.GetTimestamp() + timeoutTicks;
            bool stopped = true;
            foreach (Thread worker in workers)
            {
                long remainingTicks = Math.Max(
                    0L,
                    deadline - Stopwatch.GetTimestamp());
                int remainingMilliseconds = (int)Math.Min(
                    int.MaxValue,
                    remainingTicks * 1000L / Stopwatch.Frequency);
                if (!worker.Join(remainingMilliseconds))
                {
                    stopped = false;
                }
            }

            if (stopped)
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

            return stopped;
        }

        public void Dispose()
        {
            Shutdown(TimeSpan.FromSeconds(2));
        }

        private void WorkerLoop()
        {
            while (true)
            {
                JobHandle handle;
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

        private JobHandle TakeNextReadyJob()
        {
            for (int index = pending.Count - 1; index >= 0; index--)
            {
                JobHandle candidate = pending[index];
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

            JobHandle best = null;
            int bestIndex = -1;
            for (int index = 0; index < pending.Count; index++)
            {
                JobHandle candidate = pending[index];
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
            JobHandle handle,
            out Exception dependencyError)
        {
            dependencyError = null;
            foreach (JobHandle dependency in handle.Dependencies)
            {
                JobState state = dependency.State;
                if (state == JobState.Failed || state == JobState.Cancelled)
                {
                    dependencyError = dependency.Error ??
                        new InvalidOperationException(
                            "Job dependency failed: " + dependency.Key);
                    return false;
                }

                if (state != JobState.Completed)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ResourcesAvailable(JobHandle handle)
        {
            if ((handle.ResourceClass == JobResourceClass.Io ||
                 handle.ResourceClass == JobResourceClass.Mixed) &&
                activeIoJobs >= options.IoConcurrency)
            {
                return false;
            }

            if (handle.EstimatedBytes > 0L &&
                activeJobs > 0 &&
                activeBytes + handle.EstimatedBytes > options.ActiveByteLimit)
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

        private void Reserve(JobHandle handle)
        {
            activeJobs++;
            activeBytes += handle.EstimatedBytes;
            if (handle.ResourceClass == JobResourceClass.Io ||
                handle.ResourceClass == JobResourceClass.Mixed)
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

        private void Release(JobHandle handle)
        {
            activeJobs = Math.Max(0, activeJobs - 1);
            activeBytes = Math.Max(0L, activeBytes - handle.EstimatedBytes);
            if (handle.ResourceClass == JobResourceClass.Io ||
                handle.ResourceClass == JobResourceClass.Mixed)
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

        private static int Compare(JobHandle left, JobHandle right)
        {
            int lifetime = left.Lifetime.CompareTo(right.Lifetime);
            if (lifetime != 0)
            {
                return lifetime;
            }

            int priority = left.Priority.CompareTo(right.Priority);
            return priority != 0
                ? priority
                : left.Sequence.CompareTo(right.Sequence);
        }

        private void ValidateDependencies(
            IReadOnlyList<JobHandle> dependencies)
        {
            foreach (JobHandle dependency in dependencies)
            {
                if (!ReferenceEquals(dependency.Owner, this))
                {
                    throw new ArgumentException(
                        "Job dependencies must belong to the same scheduler.",
                        nameof(dependencies));
                }
            }
        }

        private void RemoveTerminalJob(JobHandle handle)
        {
            if (!handle.IsTerminal)
            {
                return;
            }

            if (jobsByKey.TryGetValue(handle.Key, out JobHandle current) &&
                ReferenceEquals(current, handle))
            {
                jobsByKey.Remove(handle.Key);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(JobScheduler));
            }
        }
    }
}
