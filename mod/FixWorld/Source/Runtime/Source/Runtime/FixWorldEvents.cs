using System;
using System.Threading;
using FixWorld.Lifecycle;
using FixWorld.Loading;
using Verse;

namespace FixWorld.Runtime
{
    internal static class FixWorldEvents
    {
        private const int MaximumLoadingEventsPerPump = 1024;
        private const int MaximumLifecycleEventsPerPump = 64;
        private static readonly object Sync = new object();

        private static FixWorldEventBus bus;
        private static bool stopped;

        internal static void Initialize()
        {
            lock (Sync)
            {
                if (bus != null)
                {
                    return;
                }

                if (stopped)
                {
                    throw new ObjectDisposedException(nameof(FixWorldEvents));
                }

                FixWorldEventBus created = new FixWorldEventBus();
                created.Register<LoadingStageEvent>(
                    MaximumLoadingEventsPerPump,
                    exception => Log.Error(
                        "[FixWorld] Loading event subscriber failed: " + exception));
                created.Register<RimWorldLifecycleEvent>(
                    MaximumLifecycleEventsPerPump,
                    exception => Log.Error(
                        "[FixWorld] Lifecycle event subscriber failed: " + exception));
                Volatile.Write(ref bus, created);
            }
        }

        internal static IDisposable Subscribe<TEvent>(Action<TEvent> subscriber)
        {
            return RequireBus().Subscribe(subscriber);
        }

        internal static void Publish<TEvent>(TEvent item)
        {
            RequireBus().Publish(item);
        }

        internal static void PublishLatest<TEvent>(string key, TEvent item)
        {
            RequireBus().PublishLatest(key, item);
        }

        internal static int Pump()
        {
            return RequireBus().Pump();
        }

        internal static void Shutdown()
        {
            FixWorldEventBus current;
            lock (Sync)
            {
                if (stopped)
                {
                    return;
                }

                stopped = true;
                current = Volatile.Read(ref bus);
                Volatile.Write(ref bus, null);
            }

            current?.Dispose();
        }

        private static FixWorldEventBus RequireBus()
        {
            return Volatile.Read(ref bus) ??
                   throw new InvalidOperationException(
                       "FixWorld event bus is not running.");
        }
    }
}
