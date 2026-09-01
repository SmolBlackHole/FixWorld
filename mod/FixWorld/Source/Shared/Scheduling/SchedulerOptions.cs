using System;

namespace FixWorld.Scheduling
{
    public sealed class JobSchedulerOptions
    {
        public JobSchedulerOptions(
            int workerCount,
            int ioConcurrency,
            int queueCapacity,
            long activeByteLimit,
            string workerNamePrefix = "FixWorld Worker")
        {
            if (workerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            }

            if (ioConcurrency <= 0 || ioConcurrency > workerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(ioConcurrency));
            }

            if (queueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            }

            if (activeByteLimit <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(activeByteLimit));
            }

            WorkerCount = workerCount;
            IoConcurrency = ioConcurrency;
            QueueCapacity = queueCapacity;
            ActiveByteLimit = activeByteLimit;
            WorkerNamePrefix = string.IsNullOrWhiteSpace(workerNamePrefix)
                ? "Worker"
                : workerNamePrefix;
        }

        public int WorkerCount { get; }

        public int IoConcurrency { get; }

        public int QueueCapacity { get; }

        public long ActiveByteLimit { get; }

        public string WorkerNamePrefix { get; }
    }
}
