using System;
using System.Threading;

namespace FixWorld.Scheduling
{
    internal static class FixWorldScheduler
    {
        private static readonly object Sync = new object();
        private static SchedulerRuntime runtime;
        private static bool stopped;

        internal static int WorkerCount =>
            Volatile.Read(ref runtime)?.WorkerCount ?? 0;

        internal static void Initialize()
        {
            lock (Sync)
            {
                if (stopped)
                {
                    throw new ObjectDisposedException(nameof(FixWorldScheduler));
                }

                runtime ??= SchedulerRuntime.CreateDefault();
            }
        }

        internal static ScheduledJobHandle<TResult> Schedule<TResult>(
            SchedulerJob<TResult> job)
        {
            return RequireRuntime().Schedule(job);
        }

        internal static MainThreadActionHandle Dispatch(
            string key,
            string name,
            Action action)
        {
            return RequireRuntime().Dispatch(key, name, action);
        }

        internal static void BindMainThread()
        {
            RequireRuntime().BindMainThread();
        }

        internal static int PumpMainThread(
            int maximumActions = 64,
            int maximumMilliseconds = 4)
        {
            SchedulerRuntime current = Volatile.Read(ref runtime);
            return current?.PumpMainThread(
                maximumActions,
                maximumMilliseconds) ?? 0;
        }

        internal static void Cancel(ScheduledJobHandle handle)
        {
            Volatile.Read(ref runtime)?.Cancel(handle);
        }

        internal static void Shutdown()
        {
            SchedulerRuntime current;
            lock (Sync)
            {
                if (stopped)
                {
                    return;
                }

                stopped = true;
                current = runtime;
                runtime = null;
            }

            current?.Dispose();
        }

        private static SchedulerRuntime RequireRuntime()
        {
            SchedulerRuntime current = Volatile.Read(ref runtime);
            if (current != null)
            {
                return current;
            }

            lock (Sync)
            {
                if (stopped)
                {
                    throw new ObjectDisposedException(nameof(FixWorldScheduler));
                }

                return runtime ?? throw new InvalidOperationException(
                    "FixWorld scheduler has not been initialized.");
            }
        }
    }
}
