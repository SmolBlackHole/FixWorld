using System;
using System.Collections.Generic;
using System.Threading;

namespace FixWorld.Scheduling
{
    public enum JobLifetime
    {
        Critical,
        Background
    }

    public enum JobPriority
    {
        High,
        Normal,
        Low
    }

    public enum JobResourceClass
    {
        Cpu,
        Io,
        Mixed
    }

    public enum JobState
    {
        Queued,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public sealed class Job<TResult>
    {
        private static readonly IReadOnlyList<JobHandle> NoDependencies =
            Array.Empty<JobHandle>();

        public Job(
            string key,
            Func<CancellationToken, TResult> execute,
            string name = null,
            JobLifetime lifetime = JobLifetime.Background,
            JobPriority priority = JobPriority.Normal,
            JobResourceClass resourceClass = JobResourceClass.Cpu,
            IReadOnlyList<JobHandle> dependencies = null,
            long estimatedBytes = 0L,
            string concurrencyKey = null,
            int maxConcurrency = 0)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A job requires a stable key.",
                    nameof(key));
            }

            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            if (estimatedBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(estimatedBytes));
            }

            if (maxConcurrency < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            }

            if (maxConcurrency > 0 && string.IsNullOrWhiteSpace(concurrencyKey))
            {
                throw new ArgumentException(
                    "A concurrency limit requires a concurrency key.",
                    nameof(concurrencyKey));
            }

            Key = key;
            Name = string.IsNullOrWhiteSpace(name) ? key : name;
            Execute = execute;
            Lifetime = lifetime;
            Priority = priority;
            ResourceClass = resourceClass;
            Dependencies = CopyDependencies(dependencies);
            EstimatedBytes = estimatedBytes;
            ConcurrencyKey = concurrencyKey;
            MaxConcurrency = maxConcurrency;
        }

        public string Key { get; }

        public string Name { get; }

        public JobLifetime Lifetime { get; }

        public JobPriority Priority { get; }

        public JobResourceClass ResourceClass { get; }

        public IReadOnlyList<JobHandle> Dependencies { get; }

        public long EstimatedBytes { get; }

        public string ConcurrencyKey { get; }

        public int MaxConcurrency { get; }

        internal Func<CancellationToken, TResult> Execute { get; }

        private static IReadOnlyList<JobHandle> CopyDependencies(
            IReadOnlyList<JobHandle> dependencies)
        {
            if (dependencies == null || dependencies.Count == 0)
            {
                return NoDependencies;
            }

            JobHandle[] copy = new JobHandle[dependencies.Count];
            for (int index = 0; index < copy.Length; index++)
            {
                copy[index] = dependencies[index] ??
                    throw new ArgumentException(
                        "Job dependencies cannot contain null.",
                        nameof(dependencies));
            }

            return copy;
        }
    }
}
