using System;
using FixWorld.Events;
using FixWorld.Scheduling;
using FixWorld.Telemetry;

namespace FixWorld.Runtime
{
    // Owned once by the runtime; modules borrow only the services they need.
    public sealed class RuntimeServices : IDisposable
    {
        private readonly object sync = new();
        private bool disposed;
        private bool workersStopped;

        public RuntimeServices(JobSchedulerOptions options,
            Action<string, Exception> reportMainThreadError)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Events = new EventBus();
            Telemetry = new TelemetryStore();
            try
            {
                MainThread = new MainThreadQueue(options.QueueCapacity, reportMainThreadError);
                Scheduler = new JobScheduler(options);
            }
            catch
            {
                MainThread?.Dispose();
                Telemetry.Dispose();
                Events.Dispose();
                throw;
            }
        }

        public EventBus Events { get; }
        public TelemetryStore Telemetry { get; }
        public JobScheduler Scheduler { get; }
        public MainThreadQueue MainThread { get; }

        // Call after detaching hooks and stopping modules/producers.
        public bool Shutdown(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            lock (sync)
            {
                if (disposed)
                {
                    return workersStopped;
                }

                disposed = true;
                try
                {
                    workersStopped = Scheduler.Shutdown(timeout);
                }
                finally
                {
                    MainThread.Dispose();
                    Events.Dispose();
                    Telemetry.Dispose();
                }
                return workersStopped;
            }
        }

        public void Dispose() => Shutdown(TimeSpan.FromSeconds(2));
    }
}
