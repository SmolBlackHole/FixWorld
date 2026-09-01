using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace FixWorld.Scheduling
{
    public sealed class MainThreadQueue : IDisposable
    {
        private readonly object sync = new();
        private readonly ConcurrentQueue<QueuedAction> actions =
            new ConcurrentQueue<QueuedAction>();
        private readonly Action<string, Exception> reportError;
        private readonly int capacity;
        private int mainThreadId;
        private int queued;
        private bool disposed;

        public MainThreadQueue(
            int capacity,
            Action<string, Exception> reportError)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            this.capacity = capacity;
            this.reportError = reportError ??
                throw new ArgumentNullException(nameof(reportError));
        }

        public int PendingCount => Volatile.Read(ref queued);

        public void BindCurrentThread()
        {
            int current = Thread.CurrentThread.ManagedThreadId;
            int existing = Interlocked.CompareExchange(
                ref mainThreadId,
                current,
                0);
            if (existing != 0 && existing != current)
            {
                throw new InvalidOperationException(
                    "The main-thread queue is already bound to another thread.");
            }
        }

        public void Post(string name, Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (sync)
            {
                ThrowIfDisposed();
                if (queued >= capacity)
                {
                    throw new InvalidOperationException(
                        "The main-thread queue is full (" + capacity + ").");
                }

                actions.Enqueue(new QueuedAction(name, action));
                Interlocked.Increment(ref queued);
            }
        }

        public int Pump(int maximumActions, TimeSpan timeBudget)
        {
            if (maximumActions <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumActions));
            }

            if (timeBudget < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeBudget));
            }

            int boundThread = Volatile.Read(ref mainThreadId);
            if (boundThread == 0)
            {
                throw new InvalidOperationException(
                    "The main-thread queue has not been bound.");
            }

            if (Thread.CurrentThread.ManagedThreadId != boundThread)
            {
                throw new InvalidOperationException(
                    "The main-thread queue can only be pumped by its bound thread.");
            }

            long budgetTicks = (long)Math.Min(
                long.MaxValue,
                timeBudget.TotalSeconds * Stopwatch.Frequency);
            long deadline = Stopwatch.GetTimestamp() + budgetTicks;
            int executed = 0;
            while (executed < maximumActions &&
                   Stopwatch.GetTimestamp() <= deadline &&
                   actions.TryDequeue(out QueuedAction item))
            {
                Interlocked.Decrement(ref queued);
                try
                {
                    item.Execute();
                }
                catch (Exception exception)
                {
                    reportError(item.Name, exception);
                }

                executed++;
            }

            return executed;
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
                while (actions.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref queued);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MainThreadQueue));
            }
        }

        private readonly struct QueuedAction
        {
            internal QueuedAction(string name, Action execute)
            {
                Name = string.IsNullOrWhiteSpace(name)
                    ? "Unnamed main-thread action"
                    : name;
                Execute = execute;
            }

            internal string Name { get; }

            internal Action Execute { get; }
        }
    }
}
