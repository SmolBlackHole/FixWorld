using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FixWorld.Scheduling
{
    internal sealed class EventMailbox<TEvent>
    {
        private readonly ConcurrentQueue<TEvent> queued =
            new ConcurrentQueue<TEvent>();
        private readonly object latestSync = new object();
        private readonly object subscriberSync = new object();
        private readonly Dictionary<string, TEvent> latest =
            new Dictionary<string, TEvent>(StringComparer.Ordinal);
        private readonly List<string> latestOrder = new List<string>();
        private readonly List<Action<TEvent>> subscribers =
            new List<Action<TEvent>>();
        private readonly int maximumQueuedPerDrain;
        private readonly Action<Exception> observerError;

        private Action<TEvent>[] subscriberSnapshot =
            Array.Empty<Action<TEvent>>();

        internal EventMailbox(
            int maximumQueuedPerDrain,
            Action<Exception> observerError = null)
        {
            if (maximumQueuedPerDrain <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumQueuedPerDrain));
            }

            this.maximumQueuedPerDrain = maximumQueuedPerDrain;
            this.observerError = observerError;
            EventMailboxPump.Register(Drain);
        }

        internal IDisposable Subscribe(Action<TEvent> subscriber)
        {
            if (subscriber == null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            lock (subscriberSync)
            {
                subscribers.Add(subscriber);
                subscriberSnapshot = subscribers.ToArray();
            }

            return new Subscription(this, subscriber);
        }

        internal void Publish(TEvent item)
        {
            queued.Enqueue(item);
        }

        internal void PublishLatest(string key, TEvent item)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A coalesced mailbox event needs a stable key.",
                    nameof(key));
            }

            lock (latestSync)
            {
                if (!latest.ContainsKey(key))
                {
                    latestOrder.Add(key);
                }

                latest[key] = item;
            }
        }

        internal int Drain()
        {
            Action<TEvent>[] currentSubscribers =
                Volatile.Read(ref subscriberSnapshot);
            int drained = 0;
            while (drained < maximumQueuedPerDrain &&
                   queued.TryDequeue(out TEvent item))
            {
                Notify(currentSubscribers, item);
                drained++;
            }

            TEvent[] coalesced;
            lock (latestSync)
            {
                coalesced = latestOrder
                    .Where(latest.ContainsKey)
                    .Select(key => latest[key])
                    .ToArray();
                latest.Clear();
                latestOrder.Clear();
            }

            foreach (TEvent item in coalesced)
            {
                Notify(currentSubscribers, item);
                drained++;
            }

            return drained;
        }

        private void Notify(Action<TEvent>[] currentSubscribers, TEvent item)
        {
            foreach (Action<TEvent> subscriber in currentSubscribers)
            {
                try
                {
                    subscriber(item);
                }
                catch (Exception exception)
                {
                    observerError?.Invoke(exception);
                }
            }
        }

        private void Unsubscribe(Action<TEvent> subscriber)
        {
            lock (subscriberSync)
            {
                subscribers.Remove(subscriber);
                subscriberSnapshot = subscribers.ToArray();
            }
        }

        private sealed class Subscription : IDisposable
        {
            private EventMailbox<TEvent> owner;
            private Action<TEvent> subscriber;

            internal Subscription(
                EventMailbox<TEvent> owner,
                Action<TEvent> subscriber)
            {
                this.owner = owner;
                this.subscriber = subscriber;
            }

            public void Dispose()
            {
                Action<TEvent> current =
                    Interlocked.Exchange(ref subscriber, null);
                EventMailbox<TEvent> currentOwner =
                    Interlocked.Exchange(ref owner, null);
                if (current != null && currentOwner != null)
                {
                    currentOwner.Unsubscribe(current);
                }
            }
        }
    }

    internal static class EventMailboxPump
    {
        private static readonly object Sync = new object();
        private static readonly List<Func<int>> Drainers = new List<Func<int>>();
        private static Func<int>[] drainerSnapshot = Array.Empty<Func<int>>();

        internal static void Register(Func<int> drain)
        {
            if (drain == null)
            {
                throw new ArgumentNullException(nameof(drain));
            }

            lock (Sync)
            {
                Drainers.Add(drain);
                drainerSnapshot = Drainers.ToArray();
            }
        }

        internal static int DrainAll()
        {
            int drained = 0;
            foreach (Func<int> drain in Volatile.Read(ref drainerSnapshot))
            {
                drained += drain();
            }

            return drained;
        }
    }
}
