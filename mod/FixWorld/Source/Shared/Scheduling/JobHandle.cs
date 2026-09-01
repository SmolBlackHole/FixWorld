using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace FixWorld.Scheduling
{
    public abstract class JobHandle
    {
        private readonly CancellationTokenSource cancellation =
            new CancellationTokenSource();
        private readonly ManualResetEventSlim completion =
            new ManualResetEventSlim(false);
        private int state = (int)JobState.Queued;
        private Exception error;
        private long startedAt;
        private long completedAt;

        internal JobHandle(
            JobScheduler owner,
            long sequence,
            string key,
            string name,
            JobLifetime lifetime,
            JobPriority priority,
            JobResourceClass resourceClass,
            IReadOnlyList<JobHandle> dependencies,
            long estimatedBytes,
            string concurrencyKey,
            int maxConcurrency)
        {
            Owner = owner;
            Sequence = sequence;
            Key = key;
            Name = name;
            Lifetime = lifetime;
            Priority = priority;
            ResourceClass = resourceClass;
            Dependencies = dependencies;
            EstimatedBytes = estimatedBytes;
            ConcurrencyKey = concurrencyKey;
            MaxConcurrency = maxConcurrency;
            QueuedAt = Stopwatch.GetTimestamp();
        }

        public string Key { get; }

        public string Name { get; }

        public JobLifetime Lifetime { get; }

        public JobPriority Priority { get; }

        public JobResourceClass ResourceClass { get; }

        public IReadOnlyList<JobHandle> Dependencies { get; }

        public long EstimatedBytes { get; }

        public string ConcurrencyKey { get; }

        public int MaxConcurrency { get; }

        public JobState State => (JobState)Volatile.Read(ref state);

        public Exception Error => Volatile.Read(ref error);

        public bool IsTerminal => IsTerminalState(State);

        public TimeSpan QueueWaitTime => Elapsed(
            QueuedAt,
            Interlocked.Read(ref startedAt),
            Interlocked.Read(ref completedAt));

        public TimeSpan ExecutionTime
        {
            get
            {
                long started = Interlocked.Read(ref startedAt);
                if (started == 0L)
                {
                    return TimeSpan.Zero;
                }

                long completed = Interlocked.Read(ref completedAt);
                return FromStopwatchTicks(
                    Math.Max(
                        0L,
                        (completed == 0L ? Stopwatch.GetTimestamp() : completed) -
                        started));
            }
        }

        public TimeSpan WallTime
        {
            get
            {
                long completed = Interlocked.Read(ref completedAt);
                return FromStopwatchTicks(
                    Math.Max(
                        0L,
                        (completed == 0L ? Stopwatch.GetTimestamp() : completed) -
                        QueuedAt));
            }
        }

        public abstract Type ResultType { get; }

        internal JobScheduler Owner { get; }

        internal long Sequence { get; }

        internal long QueuedAt { get; }

        public void Cancel()
        {
            cancellation.Cancel();
            if (Interlocked.CompareExchange(
                    ref state,
                    (int)JobState.Cancelled,
                    (int)JobState.Queued) == (int)JobState.Queued)
            {
                Interlocked.Exchange(ref completedAt, Stopwatch.GetTimestamp());
                completion.Set();
            }
        }

        public void Wait(CancellationToken cancellationToken = default)
        {
            completion.Wait(cancellationToken);
        }

        internal bool TryStart()
        {
            if (Interlocked.CompareExchange(
                    ref state,
                    (int)JobState.Running,
                    (int)JobState.Queued) != (int)JobState.Queued)
            {
                return false;
            }

            Interlocked.Exchange(ref startedAt, Stopwatch.GetTimestamp());
            return true;
        }

        internal void Run(CancellationToken schedulerCancellation)
        {
            if (State != JobState.Running)
            {
                throw new InvalidOperationException(
                    "A job must be started before execution: " + Key);
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
                    Finish(
                        linked.IsCancellationRequested
                            ? JobState.Cancelled
                            : JobState.Completed,
                        null);
                }
                catch (OperationCanceledException)
                {
                    Finish(JobState.Cancelled, null);
                }
                catch (Exception exception)
                {
                    Finish(JobState.Failed, exception);
                }
            }
        }

        internal void CancelBecauseDependencyFailed(Exception dependencyError)
        {
            if (Interlocked.CompareExchange(
                    ref state,
                    (int)JobState.Cancelled,
                    (int)JobState.Queued) != (int)JobState.Queued)
            {
                return;
            }

            Volatile.Write(
                ref error,
                dependencyError ?? new InvalidOperationException(
                    "A job dependency did not complete successfully."));
            Interlocked.Exchange(ref completedAt, Stopwatch.GetTimestamp());
            completion.Set();
        }

        internal void ThrowIfUnsuccessful()
        {
            JobState current = State;
            if (current == JobState.Completed)
            {
                return;
            }

            if (current == JobState.Failed && Error != null)
            {
                ExceptionDispatchInfo.Capture(Error).Throw();
            }

            throw new OperationCanceledException(
                "Job did not complete successfully: " + Key);
        }

        internal static bool IsTerminalState(JobState value)
        {
            return value == JobState.Completed ||
                   value == JobState.Failed ||
                   value == JobState.Cancelled;
        }

        protected abstract void ExecuteCore(CancellationToken cancellationToken);

        private void Finish(JobState finalState, Exception exception)
        {
            Volatile.Write(ref error, exception);
            Interlocked.Exchange(ref completedAt, Stopwatch.GetTimestamp());
            Volatile.Write(ref state, (int)finalState);
            completion.Set();
        }

        private static TimeSpan Elapsed(
            long queued,
            long started,
            long completed)
        {
            long end = started != 0L
                ? started
                : completed != 0L
                    ? completed
                    : Stopwatch.GetTimestamp();
            return FromStopwatchTicks(Math.Max(0L, end - queued));
        }

        private static TimeSpan FromStopwatchTicks(long ticks)
        {
            return TimeSpan.FromSeconds(
                (double)ticks / Stopwatch.Frequency);
        }
    }

    public sealed class JobHandle<TResult> : JobHandle
    {
        private readonly Func<CancellationToken, TResult> execute;
        private TResult result;

        internal JobHandle(
            JobScheduler owner,
            long sequence,
            Job<TResult> job)
            : base(
                owner,
                sequence,
                job.Key,
                job.Name,
                job.Lifetime,
                job.Priority,
                job.ResourceClass,
                job.Dependencies,
                job.EstimatedBytes,
                job.ConcurrencyKey,
                job.MaxConcurrency)
        {
            execute = job.Execute;
        }

        public override Type ResultType => typeof(TResult);

        public TResult Result
        {
            get
            {
                Wait();
                ThrowIfUnsuccessful();
                return result;
            }
        }

        protected override void ExecuteCore(CancellationToken cancellationToken)
        {
            result = execute(cancellationToken);
        }
    }
}
