using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace FixWorld.Scheduling
{
    internal enum SchedulerJobLifetime
    {
        Critical,
        Deferred,
        Background
    }

    internal enum SchedulerJobPriority
    {
        High,
        Normal,
        Low
    }

    internal enum SchedulerResourceClass
    {
        Cpu,
        Io,
        Mixed
    }

    internal enum SchedulerJobState
    {
        Queued,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    internal sealed class SchedulerJob<TResult>
    {
        private static readonly IReadOnlyList<ScheduledJobHandle> NoDependencies =
            Array.Empty<ScheduledJobHandle>();

        internal readonly string Key;
        internal readonly string Name;
        internal readonly SchedulerJobLifetime Lifetime;
        internal readonly SchedulerJobPriority Priority;
        internal readonly SchedulerResourceClass ResourceClass;
        internal readonly long EstimatedBytes;
        internal readonly string ConcurrencyKey;
        internal readonly int MaxConcurrency;
        internal readonly IReadOnlyList<ScheduledJobHandle> Dependencies;
        internal readonly Func<CancellationToken, TResult> Execute;

        internal SchedulerJob(
            string key,
            string name,
            SchedulerJobLifetime lifetime,
            SchedulerJobPriority priority,
            SchedulerResourceClass resourceClass,
            Func<CancellationToken, TResult> execute,
            IReadOnlyList<ScheduledJobHandle> dependencies = null,
            long estimatedBytes = 0L,
            string concurrencyKey = null,
            int maxConcurrency = 0)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A scheduler job needs a stable key.", nameof(key));
            }

            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            if (estimatedBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(estimatedBytes));
            }

            if (maxConcurrency < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            }

            if (maxConcurrency > 0 && string.IsNullOrWhiteSpace(concurrencyKey))
            {
                throw new ArgumentException(
                    "A concurrency limit needs a concurrency key.",
                    nameof(concurrencyKey));
            }

            Key = key;
            Name = string.IsNullOrWhiteSpace(name) ? key : name;
            Lifetime = lifetime;
            Priority = priority;
            ResourceClass = resourceClass;
            EstimatedBytes = estimatedBytes;
            ConcurrencyKey = concurrencyKey;
            MaxConcurrency = maxConcurrency;
            Dependencies = dependencies ?? NoDependencies;
            Execute = execute;
        }
    }

    internal abstract class ScheduledJobHandle
    {
        private readonly CancellationTokenSource cancellation =
            new CancellationTokenSource();
        private readonly ManualResetEventSlim completion =
            new ManualResetEventSlim(false);
        private int state = (int)SchedulerJobState.Queued;
        private Exception exception;

        internal readonly long Sequence;
        internal readonly string Key;
        internal readonly string Name;
        internal readonly SchedulerJobLifetime Lifetime;
        internal readonly SchedulerJobPriority Priority;
        internal readonly SchedulerResourceClass ResourceClass;
        internal readonly long EstimatedBytes;
        internal readonly string ConcurrencyKey;
        internal readonly int MaxConcurrency;
        internal readonly IReadOnlyList<ScheduledJobHandle> Dependencies;
        internal readonly long QueuedAt;

        private long startedAt;
        private long completedAt;

        internal SchedulerJobState State =>
            (SchedulerJobState)Volatile.Read(ref state);
        internal Exception Exception => Volatile.Read(ref exception);
        internal bool IsTerminal => IsTerminalState(State);
        internal long WaitTicks => Math.Max(0L, Interlocked.Read(ref startedAt) - QueuedAt);
        internal long ExecutionTicks
        {
            get
            {
                long started = Interlocked.Read(ref startedAt);
                return started == 0L
                    ? 0L
                    : Math.Max(0L, Interlocked.Read(ref completedAt) - started);
            }
        }
        internal long WallTicks => Math.Max(0L, Interlocked.Read(ref completedAt) - QueuedAt);
        internal abstract Type ResultType { get; }

        protected ScheduledJobHandle(
            long sequence,
            string key,
            string name,
            SchedulerJobLifetime lifetime,
            SchedulerJobPriority priority,
            SchedulerResourceClass resourceClass,
            long estimatedBytes,
            string concurrencyKey,
            int maxConcurrency,
            IReadOnlyList<ScheduledJobHandle> dependencies)
        {
            Sequence = sequence;
            Key = key;
            Name = name;
            Lifetime = lifetime;
            Priority = priority;
            ResourceClass = resourceClass;
            EstimatedBytes = estimatedBytes;
            ConcurrencyKey = concurrencyKey;
            MaxConcurrency = maxConcurrency;
            Dependencies = dependencies;
            QueuedAt = Stopwatch.GetTimestamp();
        }

        internal bool TryStart()
        {
            if (Interlocked.CompareExchange(
                    ref state,
                    (int)SchedulerJobState.Running,
                    (int)SchedulerJobState.Queued) != (int)SchedulerJobState.Queued)
            {
                return false;
            }

            Interlocked.Exchange(ref startedAt, Stopwatch.GetTimestamp());
            return true;
        }

        internal void Run(CancellationToken schedulerCancellation)
        {
            if (State != SchedulerJobState.Running)
            {
                throw new InvalidOperationException(
                    "A scheduler job must be started before it can run: " + Key);
            }

            using (CancellationTokenSource linked =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellation.Token,
                       schedulerCancellation))
            {
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    ExecuteCore(linked.Token);
                    if (linked.IsCancellationRequested)
                    {
                        Finish(SchedulerJobState.Cancelled, null);
                    }
                    else
                    {
                        Finish(SchedulerJobState.Completed, null);
                    }
                }
                catch (OperationCanceledException)
                {
                    Finish(SchedulerJobState.Cancelled, null);
                }
                catch (Exception error)
                {
                    Finish(SchedulerJobState.Failed, error);
                }
            }
        }

        internal void Cancel()
        {
            cancellation.Cancel();
            if (Interlocked.CompareExchange(
                    ref state,
                    (int)SchedulerJobState.Cancelled,
                    (int)SchedulerJobState.Queued) == (int)SchedulerJobState.Queued)
            {
                Interlocked.Exchange(ref completedAt, Stopwatch.GetTimestamp());
                completion.Set();
            }
        }

        internal void CancelBecauseDependencyFailed(Exception dependencyError)
        {
            if (Interlocked.CompareExchange(
                    ref state,
                    (int)SchedulerJobState.Cancelled,
                    (int)SchedulerJobState.Queued) != (int)SchedulerJobState.Queued)
            {
                return;
            }

            Volatile.Write(
                ref exception,
                dependencyError ?? new InvalidOperationException(
                    "A scheduler job dependency did not complete successfully."));
            Interlocked.Exchange(ref completedAt, Stopwatch.GetTimestamp());
            completion.Set();
        }

        internal void Wait(CancellationToken cancellationToken = default)
        {
            completion.Wait(cancellationToken);
        }

        internal void ThrowIfFailed()
        {
            SchedulerJobState current = State;
            if (current == SchedulerJobState.Completed)
            {
                return;
            }

            if (current == SchedulerJobState.Failed && Exception != null)
            {
                ExceptionDispatchInfo.Capture(Exception).Throw();
            }

            throw new OperationCanceledException(
                "Scheduler job did not complete: " + Key);
        }

        internal static bool IsTerminalState(SchedulerJobState value)
        {
            return value == SchedulerJobState.Completed ||
                   value == SchedulerJobState.Failed ||
                   value == SchedulerJobState.Cancelled;
        }

        protected abstract void ExecuteCore(CancellationToken cancellationToken);

        private void Finish(SchedulerJobState finalState, Exception error)
        {
            Volatile.Write(ref exception, error);
            Interlocked.Exchange(ref completedAt, Stopwatch.GetTimestamp());
            Volatile.Write(ref state, (int)finalState);
            completion.Set();
        }
    }

    internal sealed class ScheduledJobHandle<TResult> : ScheduledJobHandle
    {
        private readonly Func<CancellationToken, TResult> execute;
        private TResult result;

        internal TResult Result
        {
            get
            {
                ThrowIfFailed();
                return result;
            }
        }

        internal override Type ResultType => typeof(TResult);

        internal ScheduledJobHandle(long sequence, SchedulerJob<TResult> job)
            : base(
                sequence,
                job.Key,
                job.Name,
                job.Lifetime,
                job.Priority,
                job.ResourceClass,
                job.EstimatedBytes,
                job.ConcurrencyKey,
                job.MaxConcurrency,
                job.Dependencies)
        {
            execute = job.Execute;
        }

        protected override void ExecuteCore(CancellationToken cancellationToken)
        {
            result = execute(cancellationToken);
        }
    }

    internal sealed class MainThreadActionHandle
    {
        private readonly Action execute;
        private int state = (int)SchedulerJobState.Queued;
        private Exception exception;
        private long startedAt;
        private long completedAt;

        internal readonly string Key;
        internal readonly string Name;
        internal readonly long QueuedAt = Stopwatch.GetTimestamp();

        internal SchedulerJobState State =>
            (SchedulerJobState)Volatile.Read(ref state);
        internal Exception Exception => Volatile.Read(ref exception);
        internal bool IsTerminal => ScheduledJobHandle.IsTerminalState(State);
        internal long WaitTicks => Math.Max(0L, Interlocked.Read(ref startedAt) - QueuedAt);
        internal long ExecutionTicks
        {
            get
            {
                long started = Interlocked.Read(ref startedAt);
                return started == 0L
                    ? 0L
                    : Math.Max(0L, Interlocked.Read(ref completedAt) - started);
            }
        }

        internal MainThreadActionHandle(string key, string name, Action execute)
        {
            Key = key;
            Name = string.IsNullOrWhiteSpace(name) ? key : name;
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        internal void Run()
        {
            if (Interlocked.CompareExchange(
                    ref state,
                    (int)SchedulerJobState.Running,
                    (int)SchedulerJobState.Queued) != (int)SchedulerJobState.Queued)
            {
                return;
            }

            Interlocked.Exchange(ref startedAt, Stopwatch.GetTimestamp());
            try
            {
                execute();
                Volatile.Write(ref state, (int)SchedulerJobState.Completed);
            }
            catch (Exception error)
            {
                Volatile.Write(ref exception, error);
                Volatile.Write(ref state, (int)SchedulerJobState.Failed);
            }
            finally
            {
                Interlocked.Exchange(ref completedAt, Stopwatch.GetTimestamp());
            }
        }

        internal void Cancel()
        {
            if (Interlocked.CompareExchange(
                    ref state,
                    (int)SchedulerJobState.Cancelled,
                    (int)SchedulerJobState.Queued) == (int)SchedulerJobState.Queued)
            {
                Interlocked.Exchange(ref completedAt, Stopwatch.GetTimestamp());
            }
        }
    }
}
