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

        internal static void Initialize(
            Action<string, Exception> mainThreadErrorHandler = null)
        {
            lock (Sync)
            {
                if (stopped)
                {
                    throw new ObjectDisposedException(nameof(FixWorldScheduler));
                }

                runtime ??= SchedulerRuntime.CreateDefault(
                    mainThreadErrorHandler);
            }
        }

        internal static ScheduledJobHandle<TResult> Schedule<TResult>(
            SchedulerJob<TResult> job)
        {
            return RequireRuntime().Schedule(job);
        }

        internal static void Post(
            string name,
            Action action)
        {
            RequireRuntime().Post(name, action);
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

        internal static bool Shutdown()
        {
            SchedulerRuntime current;
            lock (Sync)
            {
                if (stopped)
                {
                    return true;
                }

                stopped = true;
                current = runtime;
                runtime = null;
            }

            return current?.Shutdown(2000) ?? true;
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
