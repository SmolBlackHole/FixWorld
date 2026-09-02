using System;
using System.Collections.Generic;
using System.Threading;
using FixWorld.Caching;
using FixWorld.Events;
using FixWorld.Profiling;
using FixWorld.Scheduling;

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        try
        {
            CacheSnapshotsAreImmutable();
            ProfilingAggregatesImmutableSnapshots();
            ProfileScopesCompleteExactlyOnce();
            ProfilingIsThreadSafe();
            EventBusKeepsChannelsTypedAndBounded();
            EventBusCoalescesLatestValues();
            EventBusAcceptsConcurrentPublishers();
            EventBusIsolatesSubscribersAndStopsTerminally();
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
        KeyValuePair<string, CacheEntry<string, int>>[] pending =
            cache.Writer.SnapshotEntries();
        Assert(
            pending.Length == 2,
            "The writer snapshot omitted an unpublished entry.");
        pending[0] = default;
        Assert(
            cache.Writer.TryGet("a", out CacheEntry<string, int> retained) &&
            retained.Value == "one",
            "Changing a writer snapshot changed the cache writer.");
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

    private static void ProfilingAggregatesImmutableSnapshots()
    {
        Profiler<string> profiler = new Profiler<string>(
            StringComparer.OrdinalIgnoreCase);
        profiler.Observe("parse", TimeSpan.FromMilliseconds(10));
        profiler.Observe(
            "PARSE",
            TimeSpan.FromMilliseconds(30),
            succeeded: false);

        ProfileSnapshot<string> original = profiler.Snapshot();
        Assert(original.Count == 1, "Equivalent profile keys were separated.");
        Assert(
            original.TryGet(
                "parse",
                out ProfileMeasurement<string> measurement),
            "Profile measurement was not published.");
        Assert(measurement.Calls == 2, "Profile call count is incorrect.");
        Assert(measurement.Failures == 1, "Profile failure count is incorrect.");
        Assert(
            measurement.TotalTime == TimeSpan.FromMilliseconds(40),
            "Profile total duration is incorrect.");
        Assert(
            measurement.MinimumTime == TimeSpan.FromMilliseconds(10),
            "Profile minimum duration is incorrect.");
        Assert(
            measurement.MaximumTime == TimeSpan.FromMilliseconds(30),
            "Profile maximum duration is incorrect.");
        Assert(
            measurement.AverageTime == TimeSpan.FromMilliseconds(20),
            "Profile average duration is incorrect.");

        profiler.Observe("parse", TimeSpan.FromMilliseconds(50));
        Assert(
            measurement.Calls == 2 &&
            measurement.TotalTime == TimeSpan.FromMilliseconds(40),
            "A later observation changed an existing profile snapshot.");
        Assert(
            profiler.Snapshot().TryGet(
                "parse",
                out ProfileMeasurement<string> changed) &&
            changed.Calls == 3 &&
            changed.TotalTime == TimeSpan.FromMilliseconds(90),
            "A later profile snapshot omitted an observation.");
        AssertThrows<ArgumentOutOfRangeException>(() =>
            profiler.Observe("invalid", TimeSpan.FromTicks(-1L)));
        AssertThrows<ArgumentNullException>(() =>
            profiler.Observe(null, TimeSpan.Zero));
    }

    private static void ProfileScopesCompleteExactlyOnce()
    {
        Profiler<string> profiler = new Profiler<string>(StringComparer.Ordinal);
        using (profiler.Measure("success"))
        {
        }

        ProfileScope<string> failed = profiler.Measure("failure");
        failed.Fail();
        failed.Dispose();
        ProfileScope<string> completed = profiler.Measure("completed");
        completed.Complete();
        completed.Dispose();

        ProfileSnapshot<string> snapshot = profiler.Snapshot();
        Assert(
            snapshot.TryGet(
                "success",
                out ProfileMeasurement<string> success) &&
            success.Calls == 1 &&
            success.Failures == 0,
            "A successful profile scope was not recorded once.");
        Assert(
            snapshot.TryGet(
                "failure",
                out ProfileMeasurement<string> failure) &&
            failure.Calls == 1 &&
            failure.Failures == 1,
            "A failed profile scope was not recorded once.");
        Assert(
            snapshot.TryGet(
                "completed",
                out ProfileMeasurement<string> explicitCompletion) &&
            explicitCompletion.Calls == 1,
            "An explicitly completed profile scope was recorded more than once.");
    }

    private static void ProfilingIsThreadSafe()
    {
        const int ThreadCount = 4;
        const int ObservationsPerThread = 250;
        Profiler<int> profiler = new Profiler<int>();
        Thread[] threads = new Thread[ThreadCount];
        for (int threadIndex = 0; threadIndex < threads.Length; threadIndex++)
        {
            threads[threadIndex] = new Thread(() =>
            {
                for (int index = 0; index < ObservationsPerThread; index++)
                {
                    profiler.Observe(1, TimeSpan.FromTicks(1L));
                }
            });
            threads[threadIndex].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert(
            profiler.Snapshot().TryGet(
                1,
                out ProfileMeasurement<int> measurement) &&
            measurement.Calls == ThreadCount * ObservationsPerThread &&
            measurement.TotalTime == TimeSpan.FromTicks(
                ThreadCount * ObservationsPerThread),
            "Concurrent profile observations were lost.");
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

    private static void EventBusKeepsChannelsTypedAndBounded()
    {
        List<int> numbers = new List<int>();
        List<string> messages = new List<string>();
        using (EventBus bus = new EventBus())
        {
            bus.Register<int>(2);
            bus.Register<string>(2);
            bus.Subscribe<int>(numbers.Add);
            bus.Subscribe<string>(messages.Add);

            bus.Publish(1);
            bus.Publish("message");
            bus.Publish(2);
            bus.Publish(3);

            Assert(bus.Pump() == 3, "Event pump count is incorrect.");
            Assert(
                numbers.Count == 2 && numbers[0] == 1 && numbers[1] == 2,
                "An event channel did not preserve FIFO order or its limit.");
            Assert(
                messages.Count == 1 && messages[0] == "message",
                "Typed event channels leaked or omitted an event.");
            Assert(
                bus.Pump() == 1 && numbers.Count == 3 && numbers[2] == 3,
                "A bounded event remained unavailable on the next pump.");
        }
    }

    private static void EventBusCoalescesLatestValues()
    {
        List<int> values = new List<int>();
        using (EventBus bus = new EventBus())
        {
            bus.Register<int>(4);
            bus.Subscribe<int>(values.Add);

            bus.PublishLatest("first", 1);
            bus.PublishLatest("second", 2);
            bus.PublishLatest("first", 3);

            Assert(bus.Pump() == 2, "Coalesced event count is incorrect.");
            Assert(
                values.Count == 2 && values[0] == 3 && values[1] == 2,
                "Latest events lost their value or stable key order.");
            Assert(bus.Pump() == 0, "Coalesced events were delivered twice.");
        }
    }

    private static void EventBusIsolatesSubscribersAndStopsTerminally()
    {
        int errors = 0;
        int observed = 0;
        EventBus bus = new EventBus();
        bus.Register<int>(4, _ => errors++);
        IDisposable failing = bus.Subscribe<int>(_ =>
            throw new InvalidOperationException("expected"));
        bus.Subscribe<int>(value => observed += value);

        bus.Publish(2);
        Assert(bus.Pump() == 1, "A subscriber failure stopped delivery.");
        Assert(errors == 1, "A subscriber failure was not reported.");
        Assert(observed == 2, "A healthy subscriber did not receive an event.");

        failing.Dispose();
        bus.Publish(3);
        bus.Pump();
        Assert(errors == 1, "A disposed subscription received an event.");
        Assert(observed == 5, "Subscription disposal removed another observer.");

        bus.Dispose();
        AssertThrows<ObjectDisposedException>(() => bus.Pump());
        AssertThrows<ObjectDisposedException>(() => bus.Publish(4));
    }

    private static void EventBusAcceptsConcurrentPublishers()
    {
        int observed = 0;
        int sum = 0;
        using (EventBus bus = new EventBus())
        {
            bus.Register<int>(512);
            bus.Subscribe<int>(value =>
            {
                observed++;
                sum += value;
            });

            Thread[] publishers = new Thread[4];
            for (int publisher = 0; publisher < publishers.Length; publisher++)
            {
                int first = publisher * 100;
                publishers[publisher] = new Thread(() =>
                {
                    for (int offset = 0; offset < 100; offset++)
                    {
                        bus.Publish(first + offset);
                    }
                });
                publishers[publisher].Start();
            }

            foreach (Thread publisher in publishers)
            {
                publisher.Join();
            }

            Assert(bus.Pump() == 400, "Concurrent events were lost.");
            Assert(
                observed == 400 && sum == 79800,
                "Concurrent publishers produced an invalid event set.");
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
