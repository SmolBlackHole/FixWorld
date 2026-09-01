using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace FixWorld.Scheduling
{
    internal sealed class MainThreadDispatcher
    {
        private readonly object sync = new object();
        private readonly ConcurrentQueue<MainThreadAction> actions =
            new ConcurrentQueue<MainThreadAction>();
        private readonly Action<string, Exception> reportError;
        private readonly int capacity;
        private int mainThreadId;
        private int queued;
        private bool cancelled;

        internal MainThreadDispatcher(
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

        internal void Post(
            string name,
            Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (sync)
            {
                if (cancelled)
                {
                    throw new ObjectDisposedException(
                        nameof(MainThreadDispatcher));
                }

                int count = Interlocked.Increment(ref queued);
                if (count > capacity)
                {
                    Interlocked.Decrement(ref queued);
                    throw new InvalidOperationException(
                        "FixWorld main-thread dispatcher is full (" +
                        capacity + ").");
                }

                actions.Enqueue(new MainThreadAction(name, action));
            }
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
                   actions.TryDequeue(out MainThreadAction item))
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

        internal void CancelAll()
        {
            lock (sync)
            {
                cancelled = true;
                while (actions.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref queued);
                }
            }
        }

        private readonly struct MainThreadAction
        {
            internal readonly string Name;
            internal readonly Action Execute;

            internal MainThreadAction(string name, Action execute)
            {
                Name = string.IsNullOrWhiteSpace(name)
                    ? "Unnamed main-thread action"
                    : name;
                Execute = execute;
            }
        }
    }
}
