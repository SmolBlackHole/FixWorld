using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace FixWorld.Scheduling
{
    internal sealed class MainThreadDispatcher
    {
        private readonly object sync = new object();
        private readonly ConcurrentQueue<MainThreadActionHandle> actions =
            new ConcurrentQueue<MainThreadActionHandle>();
        private readonly int capacity;
        private int mainThreadId;
        private int queued;
        private bool cancelled;

        internal MainThreadDispatcher(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

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

                MainThreadActionHandle handle =
                    new MainThreadActionHandle(key, name, action);
                actions.Enqueue(handle);
                return handle;
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
                   actions.TryDequeue(out MainThreadActionHandle handle))
            {
                Interlocked.Decrement(ref queued);
                handle.Run();
                executed++;
            }

            return executed;
        }

        internal void CancelAll()
        {
            lock (sync)
            {
                cancelled = true;
                while (actions.TryDequeue(out MainThreadActionHandle handle))
                {
                    Interlocked.Decrement(ref queued);
                    handle.Cancel();
                }
            }
        }
    }
}
