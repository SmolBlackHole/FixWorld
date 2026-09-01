using System;
using System.Collections.Generic;
using System.Threading;
using FixWorld.Caching;
using FixWorld.Scheduling;

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        try
        {
            CacheSnapshotsAreImmutable();
            SchedulerDeduplicatesAndReusesKeys();
            SchedulerHonorsDependencies();
            FailedDependenciesCancelChildren();
            SchedulerHonorsPriority();
            SchedulerAppliesBackpressure();
            CancelledQueuedJobsHaveNoExecutionTime();
            SchedulerShutdownIsTerminal();
            MainThreadQueueIsFifoAndIsolatesErrors();
            Console.WriteLine(
                "FixWorld shared contracts passed: " + assertions + ".");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void CacheSnapshotsAreImmutable()
    {
        Dictionary<string, CacheEntry<string, int>> initial =
            new Dictionary<string, CacheEntry<string, int>>(
                StringComparer.Ordinal)
            {
                ["a"] = new CacheEntry<string, int>("one", 1)
            };
        SnapshotCache<string, string, int> cache =
            new SnapshotCache<string, string, int>(
                initial,
                StringComparer.Ordinal);
        CacheSnapshot<string, string, int> original = cache.Snapshot;

        cache.Writer.Upsert("b", "two", 2);
        Assert(original.Count == 1, "A pending write changed a snapshot.");
        Assert(
            ReferenceEquals(cache.Snapshot, original),
            "A pending write was published implicitly.");

        CacheSnapshot<string, string, int> published = cache.Writer.Publish();
        Assert(published.Count == 2, "Publish omitted a cache entry.");
        cache.Writer.Upsert("a", "changed", 3);
        Assert(
            published.TryGet("a", out CacheEntry<string, int> oldEntry) &&
            oldEntry.Value == "one" &&
            oldEntry.Stamp == 1,
            "A writer mutation changed an existing snapshot.");

        CacheSnapshot<string, string, int> changed = cache.Writer.Publish();
        Assert(
            changed.TryGet("a", out CacheEntry<string, int> changedEntry) &&
            changedEntry.Value == "changed" &&
            changedEntry.Stamp == 3,
            "A published cache update was lost.");
        Assert(cache.Writer.Remove("b"), "Cache removal failed.");
        CacheSnapshot<string, string, int> removed = cache.Writer.Publish();
        Assert(removed.Count == 1, "Removed cache entry remained visible.");
        Assert(
            ReferenceEquals(cache.Writer.Publish(), removed),
            "An unchanged writer published a redundant snapshot.");
    }

    private static void SchedulerDeduplicatesAndReusesKeys()
    {
        using (JobScheduler scheduler = Scheduler(1))
        using (ManualResetEventSlim release = new ManualResetEventSlim(false))
        {
            JobHandle<int> first = scheduler.Schedule(
                new Job<int>("same", _ =>
                {
                    release.Wait();
                    return 1;
                }));
            JobHandle<int> duplicate = scheduler.Schedule(
                new Job<int>("same", _ => 99));
            Assert(
                ReferenceEquals(first, duplicate),
                "An active key was not deduplicated.");
            release.Set();
            Assert(first.Result == 1, "The original job result changed.");

            JobHandle<int> replacement = scheduler.Schedule(
                new Job<int>("same", _ => 2));
            Assert(
                !ReferenceEquals(first, replacement),
                "A terminal key was not reusable.");
            Assert(replacement.Result == 2, "Replacement job did not run.");
        }
    }

    private static void SchedulerHonorsDependencies()
    {
        using (JobScheduler scheduler = Scheduler(2))
        {
            List<string> order = new List<string>();
            JobHandle<int> parent = scheduler.Schedule(
                new Job<int>("parent", _ =>
                {
                    lock (order)
                    {
                        order.Add("parent");
                    }

                    return 1;
                }));
            JobHandle<int> child = scheduler.Schedule(
                new Job<int>(
                    "child",
                    _ =>
                    {
                        lock (order)
                        {
                            order.Add("child");
                        }

                        return 2;
                    },
                    dependencies: new JobHandle[] { parent }));

            Assert(child.Result == 2, "Dependent job did not run.");
            lock (order)
            {
                Assert(
                    order.Count == 2 &&
                    order[0] == "parent" &&
                    order[1] == "child",
                    "A dependent job ran before its prerequisite.");
            }
        }
    }

    private static void FailedDependenciesCancelChildren()
    {
        using (JobScheduler scheduler = Scheduler(2))
        {
            int childRuns = 0;
            JobHandle<int> failed = scheduler.Schedule(
                new Job<int>(
                    "failed",
                    _ => throw new InvalidOperationException("expected")));
            JobHandle<int> child = scheduler.Schedule(
                new Job<int>(
                    "cancelled-child",
                    _ => Interlocked.Increment(ref childRuns),
                    dependencies: new JobHandle[] { failed }));

            failed.Wait();
            child.Wait();
            Assert(failed.State == JobState.Failed, "Failure was not recorded.");
            Assert(
                child.State == JobState.Cancelled,
                "A failed dependency did not cancel its child.");
            Assert(childRuns == 0, "A cancelled child executed.");
            Assert(child.Error != null, "Dependency failure was not retained.");
        }
    }

    private static void SchedulerHonorsPriority()
    {
        using (JobScheduler scheduler = Scheduler(1))
        using (ManualResetEventSlim blockerStarted = new ManualResetEventSlim(false))
        using (ManualResetEventSlim release = new ManualResetEventSlim(false))
        {
            List<string> order = new List<string>();
            JobHandle<int> blocker = scheduler.Schedule(
                new Job<int>("priority-blocker", _ =>
                {
                    blockerStarted.Set();
                    release.Wait();
                    return 0;
                }));
            blockerStarted.Wait();
            JobHandle<int> low = scheduler.Schedule(
                new Job<int>(
                    "low",
                    _ =>
                    {
                        order.Add("low");
                        return 1;
                    },
                    priority: JobPriority.Low));
            JobHandle<int> high = scheduler.Schedule(
                new Job<int>(
                    "high",
                    _ =>
                    {
                        order.Add("high");
                        return 2;
                    },
                    priority: JobPriority.High));

            release.Set();
            blocker.Wait();
            high.Wait();
            low.Wait();
            Assert(
                order.Count == 2 &&
                order[0] == "high" &&
                order[1] == "low",
                "Priority did not control ready-job selection.");
        }
    }

    private static void SchedulerAppliesBackpressure()
    {
        JobSchedulerOptions options = new JobSchedulerOptions(
            workerCount: 4,
            ioConcurrency: 4,
            queueCapacity: 16,
            activeByteLimit: 100L,
            workerNamePrefix: "Backpressure Test");
        using (JobScheduler scheduler = new JobScheduler(options))
        {
            int active = 0;
            int maximum = 0;
            JobHandle<int>[] handles = new JobHandle<int>[4];
            for (int index = 0; index < handles.Length; index++)
            {
                int captured = index;
                handles[index] = scheduler.Schedule(
                    new Job<int>(
                        "bytes-" + captured,
                        _ =>
                        {
                            int current = Interlocked.Increment(ref active);
                            UpdateMaximum(ref maximum, current);
                            Thread.Sleep(40);
                            Interlocked.Decrement(ref active);
                            return captured;
                        },
                        estimatedBytes: 70L));
            }

            foreach (JobHandle<int> handle in handles)
            {
                handle.Wait();
            }

            Assert(maximum == 1, "The active-byte limit was exceeded.");
        }
    }

    private static void CancelledQueuedJobsHaveNoExecutionTime()
    {
        using (JobScheduler scheduler = Scheduler(1))
        using (ManualResetEventSlim blockerStarted = new ManualResetEventSlim(false))
        using (ManualResetEventSlim release = new ManualResetEventSlim(false))
        {
            JobHandle<int> blocker = scheduler.Schedule(
                new Job<int>("cancel-blocker", _ =>
                {
                    blockerStarted.Set();
                    release.Wait();
                    return 0;
                }));
            blockerStarted.Wait();
            JobHandle<int> cancelled = scheduler.Schedule(
                new Job<int>("cancelled", _ => 1));
            scheduler.Cancel(cancelled);
            cancelled.Wait();

            Assert(
                cancelled.State == JobState.Cancelled,
                "A queued cancellation was not terminal.");
            Assert(
                cancelled.ExecutionTime == TimeSpan.Zero,
                "A never-started job reported execution time.");
            release.Set();
            blocker.Wait();
        }
    }

    private static void SchedulerShutdownIsTerminal()
    {
        JobScheduler scheduler = Scheduler(1);
        using (ManualResetEventSlim started = new ManualResetEventSlim(false))
        {
            JobHandle<int> running = scheduler.Schedule(
                new Job<int>("shutdown", token =>
                {
                    started.Set();
                    token.WaitHandle.WaitOne();
                    token.ThrowIfCancellationRequested();
                    return 1;
                }));
            started.Wait();
            Assert(
                scheduler.Shutdown(TimeSpan.FromSeconds(2)),
                "Scheduler workers did not stop.");
            running.Wait();
            Assert(
                running.State == JobState.Cancelled,
                "Shutdown did not cancel running work.");
            AssertThrows<ObjectDisposedException>(() =>
                scheduler.Schedule(new Job<int>("after-stop", _ => 0)));
        }

        scheduler.Dispose();
    }

    private static void MainThreadQueueIsFifoAndIsolatesErrors()
    {
        List<int> values = new List<int>();
        List<string> errors = new List<string>();
        using (MainThreadQueue queue = new MainThreadQueue(
                   4,
                   (name, _) => errors.Add(name)))
        {
            queue.BindCurrentThread();
            queue.Post("first", () => values.Add(1));
            queue.Post("failure", () =>
                throw new InvalidOperationException("expected"));
            queue.Post("second", () => values.Add(2));
            int pumped = queue.Pump(4, TimeSpan.FromSeconds(1));

            Assert(pumped == 3, "Main-thread queue omitted an action.");
            Assert(
                values.Count == 2 && values[0] == 1 && values[1] == 2,
                "Main-thread queue was not FIFO.");
            Assert(
                errors.Count == 1 && errors[0] == "failure",
                "Main-thread action failure was not isolated.");
            Assert(queue.PendingCount == 0, "Pumped actions remained queued.");
        }
    }

    private static JobScheduler Scheduler(int workers)
    {
        return new JobScheduler(new JobSchedulerOptions(
            workerCount: workers,
            ioConcurrency: workers,
            queueCapacity: 64,
            activeByteLimit: 1024L * 1024L,
            workerNamePrefix: "Primitive Test"));
    }

    private static void UpdateMaximum(ref int maximum, int current)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref maximum);
            if (current <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(
                   ref maximum,
                   current,
                   observed) != observed);
    }

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        assertions++;
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            "Expected " + typeof(TException).Name + ".");
    }
}
