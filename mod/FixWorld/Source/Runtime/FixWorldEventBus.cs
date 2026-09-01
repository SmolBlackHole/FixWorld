using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FixWorld.Runtime
{
    internal sealed class FixWorldEventBus : IDisposable
    {
        private readonly object sync = new object();
        private readonly Dictionary<Type, IEventChannel> channelsByType =
            new Dictionary<Type, IEventChannel>();
        private readonly List<IEventChannel> channels =
            new List<IEventChannel>();

        private bool disposed;

        internal void Register<TEvent>(
            int maximumQueuedPerPump,
            Action<Exception> observerError = null)
        {
            lock (sync)
            {
                ThrowIfDisposed();
                Type eventType = typeof(TEvent);
                if (channelsByType.ContainsKey(eventType))
                {
                    throw new InvalidOperationException(
                        "An event channel is already registered for " +
                        eventType.FullName + ".");
                }

                EventChannel<TEvent> channel = new EventChannel<TEvent>(
                    maximumQueuedPerPump,
                    observerError);
                channelsByType.Add(eventType, channel);
                channels.Add(channel);
            }
        }

        internal IDisposable Subscribe<TEvent>(Action<TEvent> subscriber)
        {
            return RequireChannel<TEvent>().Subscribe(subscriber);
        }

        internal void Publish<TEvent>(TEvent item)
        {
            RequireChannel<TEvent>().Publish(item);
        }

        internal void PublishLatest<TEvent>(string key, TEvent item)
        {
            RequireChannel<TEvent>().PublishLatest(key, item);
        }

        internal int Pump()
        {
            IEventChannel[] snapshot;
            lock (sync)
            {
                ThrowIfDisposed();
                snapshot = channels.ToArray();
            }

            int delivered = 0;
            foreach (IEventChannel channel in snapshot)
            {
                delivered += channel.Drain();
            }

            return delivered;
        }

        public void Dispose()
        {
            IEventChannel[] snapshot;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                snapshot = channels.ToArray();
                channels.Clear();
                channelsByType.Clear();
            }

            foreach (IEventChannel channel in snapshot)
            {
                channel.Dispose();
            }
        }

        private EventChannel<TEvent> RequireChannel<TEvent>()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (channelsByType.TryGetValue(
                        typeof(TEvent),
                        out IEventChannel channel))
                {
                    return (EventChannel<TEvent>)channel;
                }
            }

            throw new InvalidOperationException(
                "No event channel is registered for " +
                typeof(TEvent).FullName + ".");
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(FixWorldEventBus));
            }
        }

        private interface IEventChannel : IDisposable
        {
            int Drain();
        }

        private sealed class EventChannel<TEvent> : IEventChannel
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
            private readonly int maximumQueuedPerPump;
            private readonly Action<Exception> observerError;

            private Action<TEvent>[] subscriberSnapshot =
                Array.Empty<Action<TEvent>>();
            private int disposed;

            internal EventChannel(
                int maximumQueuedPerPump,
                Action<Exception> observerError)
            {
                if (maximumQueuedPerPump <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maximumQueuedPerPump));
                }

                this.maximumQueuedPerPump = maximumQueuedPerPump;
                this.observerError = observerError;
            }

            internal IDisposable Subscribe(Action<TEvent> subscriber)
            {
                if (subscriber == null)
                {
                    throw new ArgumentNullException(nameof(subscriber));
                }

                lock (subscriberSync)
                {
                    ThrowIfDisposed();
                    subscribers.Add(subscriber);
                    subscriberSnapshot = subscribers.ToArray();
                }

                return new Subscription(this, subscriber);
            }

            internal void Publish(TEvent item)
            {
                ThrowIfDisposed();
                queued.Enqueue(item);
            }

            internal void PublishLatest(string key, TEvent item)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new ArgumentException(
                        "A coalesced event needs a stable key.",
                        nameof(key));
                }

                lock (latestSync)
                {
                    ThrowIfDisposed();
                    if (!latest.ContainsKey(key))
                    {
                        latestOrder.Add(key);
                    }

                    latest[key] = item;
                }
            }

            public int Drain()
            {
                if (Volatile.Read(ref disposed) != 0)
                {
                    return 0;
                }

                Action<TEvent>[] currentSubscribers =
                    Volatile.Read(ref subscriberSnapshot);
                int delivered = 0;
                while (delivered < maximumQueuedPerPump &&
                       queued.TryDequeue(out TEvent item))
                {
                    Notify(currentSubscribers, item);
                    delivered++;
                }

                TEvent[] coalesced;
                lock (latestSync)
                {
                    coalesced = new TEvent[latestOrder.Count];
                    int count = 0;
                    foreach (string key in latestOrder)
                    {
                        if (latest.TryGetValue(key, out TEvent item))
                        {
                            coalesced[count++] = item;
                        }
                    }

                    if (count != coalesced.Length)
                    {
                        Array.Resize(ref coalesced, count);
                    }

                    latest.Clear();
                    latestOrder.Clear();
                }

                foreach (TEvent item in coalesced)
                {
                    Notify(currentSubscribers, item);
                    delivered++;
                }

                return delivered;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }

                while (queued.TryDequeue(out _))
                {
                }

                lock (latestSync)
                {
                    latest.Clear();
                    latestOrder.Clear();
                }

                lock (subscriberSync)
                {
                    subscribers.Clear();
                    subscriberSnapshot = Array.Empty<Action<TEvent>>();
                }
            }

            private void Notify(
                Action<TEvent>[] currentSubscribers,
                TEvent item)
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
                    if (Volatile.Read(ref disposed) != 0)
                    {
                        return;
                    }

                    subscribers.Remove(subscriber);
                    subscriberSnapshot = subscribers.ToArray();
                }
            }

            private void ThrowIfDisposed()
            {
                if (Volatile.Read(ref disposed) != 0)
                {
                    throw new ObjectDisposedException(
                        typeof(EventChannel<TEvent>).Name);
                }
            }

            private sealed class Subscription : IDisposable
            {
                private EventChannel<TEvent> owner;
                private Action<TEvent> subscriber;

                internal Subscription(
                    EventChannel<TEvent> owner,
                    Action<TEvent> subscriber)
                {
                    this.owner = owner;
                    this.subscriber = subscriber;
                }

                public void Dispose()
                {
                    Action<TEvent> current =
                        Interlocked.Exchange(ref subscriber, null);
                    EventChannel<TEvent> currentOwner =
                        Interlocked.Exchange(ref owner, null);
                    if (current != null && currentOwner != null)
                    {
                        currentOwner.Unsubscribe(current);
                    }
                }
            }
        }
    }
}
