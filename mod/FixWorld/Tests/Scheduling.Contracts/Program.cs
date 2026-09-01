using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FixWorld.Caching;
using FixWorld.Preloader;
using FixWorld.Runtime;
using FixWorld.Scheduling;

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        ConfigureScheduler();
        try
        {
            ActiveKeyIsDeduplicatedAndTerminalKeyCanRunAgain();
            FailedAndCancelledKeysCanRunAgain();
            DependenciesGateExecutionAndPropagateFailure();
            PriorityOrdersReadyJobs();
            ConcurrencyGroupLimitsParallelism();
            ByteBudgetLimitsParallelism();
            EventRegistrationIsExplicit();
            EventsPublishedByWorkersAreDeliveredByThePumpThread();
            EventChannelsPreserveOrderAndCoalesceLatestValues();
            EventSubscribersAreIsolatedAndDisposable();
            EventBusShutdownIsFinal();
            PreloaderSignalsBelongToTheCurrentProcess();
            RuntimeLifecycleTransitionsAreStrict();
            ConcurrentRuntimeStartRunsOnce();
            RuntimeFailureIsTerminal();
            AtomicFileReplacesAndBacksUp();
            CacheWriterPublishesImmutableSnapshots();
            MainThreadDispatcherIsFifo();
            MainThreadDispatcherReportsAndIsolatesFailures();
            DispatcherShutdownIsAtomic();
            RuntimeShutdownCancelsCooperativeWorker();
            ShutdownIsFinal();
            Console.WriteLine(
                "Runtime contracts passed; assertions=" + assertions + ".");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void PreloaderSignalsBelongToTheCurrentProcess()
    {
        string staleProcessId = (Environment.ProcessId + 1).ToString();
        Environment.SetEnvironmentVariable(
            PreloaderTimelineContract.ActiveVariable,
            staleProcessId);
        Environment.SetEnvironmentVariable(
            PreloaderTimelineContract.RuntimeOwnsModBootVariable,
            staleProcessId);

        PreloaderTimelineSnapshot stale =
            PreloaderTimelineContract.CaptureAtBootstrap(1L, 0);
        Assert(!stale.Active, "An inherited preloader signal must be stale.");
        Assert(
            !PreloaderTimelineContract.RuntimeOwnsModBoot(),
            "An inherited runtime signal must be stale.");

        PreloaderTimelineContract.PublishEntry(1L, 0);
        PreloaderTimelineContract.PublishRuntimeOwnsModBoot();
        PreloaderTimelineSnapshot current =
            PreloaderTimelineContract.CaptureAtBootstrap(1L, 0);
        Assert(current.Active, "The current preloader signal was not accepted.");
        Assert(
            PreloaderTimelineContract.RuntimeOwnsModBoot(),
            "The current runtime signal was not accepted.");
    }

    private static void RuntimeLifecycleTransitionsAreStrict()
    {
        RuntimeLifecycle lifecycle = new RuntimeLifecycle();
        object mod = new object();
        int starts = 0;
        int attaches = 0;
        int shutdowns = 0;

        AssertThrows<InvalidOperationException>(
            () => lifecycle.AttachMod(mod, () => { }));
        lifecycle.StartEarly(() => starts++);
        lifecycle.StartEarly(() => starts++);
        Assert(starts == 1, "The runtime initialized more than once.");
        Assert(
            lifecycle.Snapshot.State == FixWorldRuntimeState.EarlyReady,
            "The runtime did not become early-ready.");

        lifecycle.AttachMod(mod, () => attaches++);
        lifecycle.AttachMod(mod, () => attaches++);
        Assert(attaches == 1, "The same mod instance attached more than once.");
        Assert(
            lifecycle.Snapshot.State == FixWorldRuntimeState.Running &&
            lifecycle.Snapshot.HasAttachedMod,
            "The runtime did not enter the running state after mod attachment.");
        AssertThrows<InvalidOperationException>(
            () => lifecycle.AttachMod(new object(), () => { }));

        lifecycle.Shutdown(() => shutdowns++);
        lifecycle.Shutdown(() => shutdowns++);
        Assert(shutdowns == 1, "Runtime shutdown executed more than once.");
        Assert(
            lifecycle.Snapshot.State == FixWorldRuntimeState.Stopped,
            "Runtime shutdown was not final.");
        AssertThrows<InvalidOperationException>(
            () => lifecycle.StartEarly(() => { }));
        AssertThrows<InvalidOperationException>(
            () => lifecycle.AttachMod(mod, () => { }));
    }

    private static void ConcurrentRuntimeStartRunsOnce()
    {
        RuntimeLifecycle lifecycle = new RuntimeLifecycle();
        using (ManualResetEventSlim entered = new ManualResetEventSlim(false))
        using (ManualResetEventSlim release = new ManualResetEventSlim(false))
        {
            int starts = 0;
            Action initialize = () =>
            {
                Interlocked.Increment(ref starts);
                entered.Set();
                release.Wait();
            };
            Task first = Task.Run(() => lifecycle.StartEarly(initialize));
            Task second = Task.Run(() => lifecycle.StartEarly(initialize));

            Assert(
                entered.Wait(TimeSpan.FromSeconds(2)),
                "The concurrent runtime start did not begin.");
            Assert(
                lifecycle.Snapshot.State == FixWorldRuntimeState.Starting,
                "The runtime did not expose its starting state.");
            release.Set();
            Task.WaitAll(first, second);
            Assert(starts == 1, "Concurrent starts initialized the runtime twice.");
            Assert(
                lifecycle.Snapshot.State == FixWorldRuntimeState.EarlyReady,
                "Concurrent start did not finish in early-ready state.");
        }
    }

    private static void RuntimeFailureIsTerminal()
    {
        RuntimeLifecycle startFailure = new RuntimeLifecycle();
        AssertThrows<InvalidOperationException>(
            () => startFailure.StartEarly(
                () => throw new InvalidOperationException("expected start failure")));
        Assert(
            startFailure.Snapshot.State == FixWorldRuntimeState.Failed &&
            startFailure.Snapshot.FailureMessage.Contains("expected start failure"),
            "A failed start did not retain its terminal failure state.");
        AssertThrows<InvalidOperationException>(
            () => startFailure.StartEarly(() => { }));
        startFailure.Shutdown(() =>
            throw new InvalidOperationException("must not run"));
        Assert(
            startFailure.Snapshot.State == FixWorldRuntimeState.Failed,
            "Shutdown changed a failed runtime state.");

        RuntimeLifecycle attachFailure = new RuntimeLifecycle();
        attachFailure.StartEarly(() => { });
        AssertThrows<InvalidOperationException>(
            () => attachFailure.AttachMod(
                new object(),
                () => throw new InvalidOperationException("expected attach failure")));
        Assert(
            attachFailure.Snapshot.State == FixWorldRuntimeState.Failed &&
            attachFailure.Snapshot.HasAttachedMod,
            "A failed attachment did not enter the terminal failure state.");

        RuntimeLifecycle externalFailure = new RuntimeLifecycle();
        externalFailure.StartEarly(() => { });
        externalFailure.MarkFailed(
            new InvalidOperationException("expected mod-boot failure"));
        Assert(
            externalFailure.Snapshot.State == FixWorldRuntimeState.Failed &&
            externalFailure.Snapshot.FailureMessage.Contains("mod-boot failure"),
            "A mod-boot failure did not become terminal.");
        AssertThrows<InvalidOperationException>(
            () => externalFailure.AttachMod(new object(), () => { }));
    }

    private static void ActiveKeyIsDeduplicatedAndTerminalKeyCanRunAgain()
    {
        using (SchedulerRuntime runtime = SchedulerRuntime.CreateDefault())
        using (ManualResetEventSlim release = new ManualResetEventSlim(false))
        {
            ScheduledJobHandle<int> first = runtime.Schedule(
                Job("repeat", _ =>
                {
                    release.Wait();
                    return 1;
                }));
            Assert(
                SpinWait.SpinUntil(
                    () => first.State == SchedulerJobState.Running,
                    TimeSpan.FromSeconds(2)),
                "The first keyed job did not start.");

            ScheduledJobHandle<int> duplicate = runtime.Schedule(
                Job("repeat", _ => 99));
            Assert(
                ReferenceEquals(first, duplicate),
                "An active key must return its existing handle.");

            release.Set();
            first.Wait();
            Assert(first.Result == 1, "The first keyed result is wrong.");

            ScheduledJobHandle<int> second = runtime.Schedule(
                Job("repeat", _ => 2));
            Assert(
                !ReferenceEquals(first, second),
                "A terminal key must create a fresh handle.");
            second.Wait();
            Assert(second.Result == 2, "The repeated keyed result is wrong.");

            ScheduledJobHandle<string> changedType = runtime.Schedule(
                new SchedulerJob<string>(
                    "repeat",
                    "repeat",
                    SchedulerJobLifetime.Background,
                    SchedulerJobPriority.Low,
                    SchedulerResourceClass.Cpu,
                    _ => "fresh"));
            changedType.Wait();
            Assert(
                changedType.Result == "fresh",
                "A terminal key retained its old result type.");
        }
    }

    private static void FailedAndCancelledKeysCanRunAgain()
    {
        using (SchedulerRuntime runtime = SchedulerRuntime.CreateDefault())
        {
            ScheduledJobHandle<int> failed = runtime.Schedule(
                Job("failure", _ => throw new InvalidOperationException("expected")));
            failed.Wait();
            Assert(
                failed.State == SchedulerJobState.Failed,
                "The failing job did not enter the failed state.");

            ScheduledJobHandle<int> retry = runtime.Schedule(
                Job("failure", _ => 3));
            retry.Wait();
            Assert(retry.Result == 3, "A failed key could not be retried.");

            using (ManualResetEventSlim release = new ManualResetEventSlim(false))
            {
                ScheduledJobHandle<int> blocker = runtime.Schedule(
                    Job("blocker", _ =>
                    {
                        release.Wait();
                        return 0;
                    }));
                Assert(
                    SpinWait.SpinUntil(
                        () => blocker.State == SchedulerJobState.Running,
                        TimeSpan.FromSeconds(2)),
                    "The blocker did not start.");

                ScheduledJobHandle<int> cancelled = runtime.Schedule(
                    Job("cancelled", _ => 4));
                runtime.Cancel(cancelled);
                Assert(
                    cancelled.State == SchedulerJobState.Cancelled,
                    "The queued job was not cancelled.");

                ScheduledJobHandle<int> replacement = runtime.Schedule(
                    Job("cancelled", _ => 5));
                release.Set();
                blocker.Wait();
                replacement.Wait();
                Assert(
                    replacement.Result == 5,
                    "A cancelled key could not be retried.");
            }
        }
    }

    private static void MainThreadDispatcherIsFifo()
    {
        MainThreadDispatcher dispatcher = new MainThreadDispatcher(
            8,
            (_, exception) => throw exception);
        List<int> order = new List<int>();
        dispatcher.Post("one", () => order.Add(1));
        dispatcher.Post("two", () => order.Add(2));
        dispatcher.Post("three", () => order.Add(3));
        dispatcher.BindCurrentThread();

        Assert(
            dispatcher.Pump(2, 1000) == 2,
            "The first dispatcher pump executed the wrong action count.");
        Assert(
            order.Count == 2 && order[0] == 1 && order[1] == 2,
            "The dispatcher did not preserve FIFO order.");
        Assert(
            dispatcher.Pump(2, 1000) == 1 && order[2] == 3,
            "The dispatcher did not execute the remaining action.");
        dispatcher.CancelAll();
    }

    private static void AtomicFileReplacesAndBacksUp()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FixWorld-contract-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "state.txt");
        string backup = Path.Combine(directory, "state.backup.txt");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "old", new UTF8Encoding(false));
            AtomicFile.WriteText(path, "new", new UTF8Encoding(false));
            Assert(
                File.ReadAllText(path) == "new",
                "Atomic text writing did not publish the replacement.");

            AtomicFile.Write(
                path,
                stream =>
                {
                    byte[] bytes = Encoding.UTF8.GetBytes("newer");
                    stream.Write(bytes, 0, bytes.Length);
                },
                backup);
            Assert(
                File.ReadAllText(path) == "newer" &&
                File.ReadAllText(backup) == "new",
                "Atomic replacement did not preserve the requested backup.");
            Assert(
                Directory.GetFiles(directory, "*.tmp-*").Length == 0,
                "Atomic writing left a temporary file behind.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static void CacheWriterPublishesImmutableSnapshots()
    {
        Dictionary<string, CacheEntry<string, int>> initial =
            new Dictionary<string, CacheEntry<string, int>>(StringComparer.Ordinal)
            {
                ["first"] = new CacheEntry<string, int>("old", 1)
            };
        CacheRuntime<string, string, int> runtime =
            new CacheRuntime<string, string, int>(initial, StringComparer.Ordinal);
        CacheWriter<string, string, int> writer = runtime.Writer;
        CacheSnapshot<string, string, int> original = runtime.Snapshot;

        writer.Upsert("second", "new", 2);
        Assert(
            writer.TryGet("second", out CacheEntry<string, int> pending) &&
            pending.Value == "new" &&
            !original.TryGet("second", out _),
            "The cache writer did not isolate unpublished changes.");
        Assert(
            writer.Remove("first") &&
            !writer.TryGet("first", out _) &&
            original.TryGet("first", out CacheEntry<string, int> retained) &&
            retained.Value == "old",
            "A writer mutation changed an existing immutable snapshot.");

        CacheSnapshot<string, string, int> published = writer.Publish();
        Assert(
            ReferenceEquals(published, runtime.Snapshot) &&
            published.TryGet("second", out CacheEntry<string, int> value) &&
            value.Stamp == 2 &&
            !published.TryGet("first", out _),
            "Publishing did not expose the writer state atomically.");
        Assert(
            ReferenceEquals(published, writer.Publish()),
            "Publishing an unchanged writer created another snapshot.");

        writer.Upsert("third", "later", 3);
        Assert(
            !published.TryGet("third", out _),
            "A published snapshot changed after another writer mutation.");
    }

    private static void MainThreadDispatcherReportsAndIsolatesFailures()
    {
        List<string> errors = new List<string>();
        MainThreadDispatcher dispatcher = new MainThreadDispatcher(
            4,
            (name, exception) => errors.Add(name + ":" + exception.Message));
        int healthyExecutions = 0;
        dispatcher.Post(
            "failing",
            () => throw new InvalidOperationException("expected"));
        dispatcher.Post("healthy", () => healthyExecutions++);
        dispatcher.BindCurrentThread();

        Assert(
            dispatcher.Pump(4, 1000) == 2,
            "A failing main-thread action stopped the dispatcher pump.");
        Assert(
            errors.Count == 1 && errors[0] == "failing:expected",
            "The dispatcher did not report a main-thread action failure.");
        Assert(
            healthyExecutions == 1,
            "A healthy action after a failure was not executed.");
        dispatcher.CancelAll();
    }

    private static void EventRegistrationIsExplicit()
    {
        using (FixWorldEventBus eventBus = new FixWorldEventBus())
        {
            AssertThrows<InvalidOperationException>(
                () => eventBus.Publish(new FirstEvent(1)));
            eventBus.Register<FirstEvent>(8);
            AssertThrows<InvalidOperationException>(
                () => eventBus.Register<FirstEvent>(8));
        }
    }

    private static void EventsPublishedByWorkersAreDeliveredByThePumpThread()
    {
        using (FixWorldEventBus eventBus = new FixWorldEventBus())
        {
            eventBus.Register<FirstEvent>(8);
            int pumpThread = Thread.CurrentThread.ManagedThreadId;
            int deliveryThread = 0;
            int deliveredValue = 0;
            eventBus.Subscribe<FirstEvent>(item =>
            {
                deliveryThread = Thread.CurrentThread.ManagedThreadId;
                deliveredValue = item.Value;
            });

            Thread publisher = new Thread(
                () => eventBus.Publish(new FirstEvent(42)));
            publisher.Start();
            Assert(
                publisher.Join(TimeSpan.FromSeconds(2)),
                "The worker publisher did not finish.");
            Assert(
                deliveredValue == 0,
                "An event subscriber ran on the publishing worker.");
            Assert(
                eventBus.Pump() == 1,
                "The event pump delivered the wrong number of worker events.");
            Assert(
                deliveredValue == 42 && deliveryThread == pumpThread,
                "A worker event was not delivered by the pump thread.");
        }
    }

    private static void EventChannelsPreserveOrderAndCoalesceLatestValues()
    {
        using (FixWorldEventBus eventBus = new FixWorldEventBus())
        {
            eventBus.Register<FirstEvent>(2);
            eventBus.Register<SecondEvent>(2);
            List<string> order = new List<string>();
            eventBus.Subscribe<FirstEvent>(
                item => order.Add("first:" + item.Value));
            eventBus.Subscribe<SecondEvent>(
                item => order.Add("second:" + item.Value));

            eventBus.Publish(new SecondEvent(9));
            eventBus.PublishLatest("progress", new FirstEvent(1));
            eventBus.PublishLatest("progress", new FirstEvent(2));
            eventBus.PublishLatest("detail", new FirstEvent(3));

            Assert(
                eventBus.Pump() == 3,
                "The event pump did not coalesce latest values by key.");
            Assert(
                order.Count == 3 &&
                order[0] == "first:2" &&
                order[1] == "first:3" &&
                order[2] == "second:9",
                "Event channels did not preserve registration and key order.");

            eventBus.Publish(new FirstEvent(4));
            eventBus.Publish(new FirstEvent(5));
            eventBus.Publish(new FirstEvent(6));
            Assert(
                eventBus.Pump() == 2 && eventBus.Pump() == 1,
                "The event channel ignored its per-pump delivery budget.");
        }
    }

    private static void EventSubscribersAreIsolatedAndDisposable()
    {
        int observerErrors = 0;
        using (FixWorldEventBus eventBus = new FixWorldEventBus())
        {
            eventBus.Register<FirstEvent>(
                8,
                _ => Interlocked.Increment(ref observerErrors));
            int healthyDeliveries = 0;
            int removedDeliveries = 0;
            eventBus.Subscribe<FirstEvent>(
                _ => throw new InvalidOperationException("expected observer failure"));
            eventBus.Subscribe<FirstEvent>(
                _ => Interlocked.Increment(ref healthyDeliveries));
            IDisposable removed = eventBus.Subscribe<FirstEvent>(
                _ => Interlocked.Increment(ref removedDeliveries));
            removed.Dispose();

            eventBus.Publish(new FirstEvent(1));
            eventBus.Pump();
            Assert(
                observerErrors == 1 && healthyDeliveries == 1,
                "A failing event subscriber interrupted healthy subscribers.");
            Assert(
                removedDeliveries == 0,
                "A disposed event subscription still received an event.");
        }
    }

    private static void EventBusShutdownIsFinal()
    {
        FixWorldEventBus eventBus = new FixWorldEventBus();
        eventBus.Register<FirstEvent>(8);
        IDisposable subscription = eventBus.Subscribe<FirstEvent>(_ => { });
        eventBus.Dispose();
        subscription.Dispose();

        AssertThrows<ObjectDisposedException>(
            () => eventBus.Register<SecondEvent>(8));
        AssertThrows<ObjectDisposedException>(
            () => eventBus.Publish(new FirstEvent(1)));
        AssertThrows<ObjectDisposedException>(() => eventBus.Pump());
    }

    private static void DependenciesGateExecutionAndPropagateFailure()
    {
        ConfigureScheduler(2);
        using (SchedulerRuntime runtime = SchedulerRuntime.CreateDefault())
        using (ManualResetEventSlim release = new ManualResetEventSlim(false))
        {
            ScheduledJobHandle<int> prerequisite = runtime.Schedule(
                Job("dependency-parent", _ =>
                {
                    release.Wait();
                    return 7;
                }));
            Assert(
                SpinWait.SpinUntil(
                    () => prerequisite.State == SchedulerJobState.Running,
                    TimeSpan.FromSeconds(2)),
                "The dependency prerequisite did not start.");

            int childExecutions = 0;
            ScheduledJobHandle<int> child = runtime.Schedule(
                Job(
                    "dependency-child",
                    _ =>
                    {
                        Interlocked.Increment(ref childExecutions);
                        return 8;
                    },
                    dependencies: new ScheduledJobHandle[] { prerequisite }));
            Assert(
                !SpinWait.SpinUntil(
                    () => child.State != SchedulerJobState.Queued,
                    TimeSpan.FromMilliseconds(150)),
                "A dependent job ran before its prerequisite completed.");

            release.Set();
            prerequisite.Wait();
            child.Wait();
            Assert(
                child.Result == 8 && childExecutions == 1,
                "A completed dependency did not release its child exactly once.");

            ScheduledJobHandle<int> failed = runtime.Schedule(
                Job(
                    "dependency-failure",
                    _ => throw new InvalidOperationException("expected dependency failure")));
            ScheduledJobHandle<int> cancelled = runtime.Schedule(
                Job(
                    "dependency-cancelled-child",
                    _ => 9,
                    dependencies: new ScheduledJobHandle[] { failed }));
            failed.Wait();
            cancelled.Wait();
            Assert(
                cancelled.State == SchedulerJobState.Cancelled &&
                cancelled.Exception != null,
                "A failed dependency did not cancel its child with an error.");
        }
    }

    private static void PriorityOrdersReadyJobs()
    {
        ConfigureScheduler(1);
        using (SchedulerRuntime runtime = SchedulerRuntime.CreateDefault())
        using (ManualResetEventSlim release = new ManualResetEventSlim(false))
        {
            ScheduledJobHandle<int> blocker = runtime.Schedule(
                Job("priority-blocker", _ =>
                {
                    release.Wait();
                    return 0;
                }));
            Assert(
                SpinWait.SpinUntil(
                    () => blocker.State == SchedulerJobState.Running,
                    TimeSpan.FromSeconds(2)),
                "The priority blocker did not start.");

            List<string> order = new List<string>();
            ScheduledJobHandle<int> low = runtime.Schedule(
                Job("priority-low", _ =>
                {
                    lock (order)
                    {
                        order.Add("low");
                    }

                    return 1;
                }));
            ScheduledJobHandle<int> high = runtime.Schedule(
                Job(
                    "priority-high",
                    _ =>
                    {
                        lock (order)
                        {
                            order.Add("high");
                        }

                        return 2;
                    },
                    priority: SchedulerJobPriority.High));

            release.Set();
            blocker.Wait();
            high.Wait();
            low.Wait();
            Assert(
                order.Count == 2 && order[0] == "high" && order[1] == "low",
                "Ready jobs did not honor scheduler priority.");
        }
    }

    private static void ConcurrencyGroupLimitsParallelism()
    {
        ConfigureScheduler(2);
        using (SchedulerRuntime runtime = SchedulerRuntime.CreateDefault())
        using (ManualResetEventSlim release = new ManualResetEventSlim(false))
        {
            ScheduledJobHandle<int> first = runtime.Schedule(
                Job(
                    "group-first",
                    _ =>
                    {
                        release.Wait();
                        return 1;
                    },
                    concurrencyKey: "limited",
                    maxConcurrency: 1));
            Assert(
                SpinWait.SpinUntil(
                    () => first.State == SchedulerJobState.Running,
                    TimeSpan.FromSeconds(2)),
                "The first concurrency-group job did not start.");

            int secondExecutions = 0;
            ScheduledJobHandle<int> second = runtime.Schedule(
                Job(
                    "group-second",
                    _ =>
                    {
                        Interlocked.Increment(ref secondExecutions);
                        return 2;
                    },
                    concurrencyKey: "limited",
                    maxConcurrency: 1));
            ScheduledJobHandle<int> control = runtime.Schedule(
                Job(
                    "group-control",
                    _ => 3,
                    concurrencyKey: "independent",
                    maxConcurrency: 1));
            control.Wait();
            Assert(
                second.State == SchedulerJobState.Queued && secondExecutions == 0,
                "A concurrency group exceeded its configured limit.");

            release.Set();
            first.Wait();
            second.Wait();
            Assert(
                second.Result == 2 && secondExecutions == 1,
                "The queued concurrency-group job did not resume.");
        }
    }

    private static void ByteBudgetLimitsParallelism()
    {
        const long TwelveMiB = 12L * 1024L * 1024L;
        ConfigureScheduler(2, TwelveMiB + 4L * 1024L * 1024L);
        using (SchedulerRuntime runtime = SchedulerRuntime.CreateDefault())
        using (ManualResetEventSlim release = new ManualResetEventSlim(false))
        {
            ScheduledJobHandle<int> first = runtime.Schedule(
                Job(
                    "bytes-first",
                    _ =>
                    {
                        release.Wait();
                        return 1;
                    },
                    estimatedBytes: TwelveMiB));
            Assert(
                SpinWait.SpinUntil(
                    () => first.State == SchedulerJobState.Running,
                    TimeSpan.FromSeconds(2)),
                "The first byte-budget job did not start.");

            int secondExecutions = 0;
            ScheduledJobHandle<int> second = runtime.Schedule(
                Job(
                    "bytes-second",
                    _ =>
                    {
                        Interlocked.Increment(ref secondExecutions);
                        return 2;
                    },
                    estimatedBytes: TwelveMiB));
            ScheduledJobHandle<int> control = runtime.Schedule(
                Job("bytes-control", _ => 3));
            control.Wait();
            Assert(
                second.State == SchedulerJobState.Queued && secondExecutions == 0,
                "Jobs exceeded the configured in-flight byte budget.");

            release.Set();
            first.Wait();
            second.Wait();
            Assert(
                second.Result == 2 && secondExecutions == 1,
                "The byte-budgeted job did not resume after capacity was released.");
        }
    }

    private static void DispatcherShutdownIsAtomic()
    {
        MainThreadDispatcher dispatcher = new MainThreadDispatcher(
            1,
            (_, exception) => throw exception);
        AssertThrows<ArgumentNullException>(
            () => dispatcher.Post("invalid", null));
        int executions = 0;
        dispatcher.Post(
            "queued",
            () => executions++);
        dispatcher.CancelAll();
        Assert(
            dispatcher.Pump(1, 1000) == 0 && executions == 0,
            "Dispatcher shutdown did not discard an existing action.");
        AssertThrows<ObjectDisposedException>(
            () => dispatcher.Post("late", () => { }));
    }

    private static void RuntimeShutdownCancelsCooperativeWorker()
    {
        SchedulerRuntime runtime = SchedulerRuntime.CreateDefault();
        ScheduledJobHandle<int> running = runtime.Schedule(
            Job("shutdown-worker", cancellationToken =>
            {
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return 0;
            }));
        Assert(
            SpinWait.SpinUntil(
                () => running.State == SchedulerJobState.Running,
                TimeSpan.FromSeconds(2)),
            "The cooperative shutdown job did not start.");
        runtime.Dispose();
        running.Wait();
        Assert(
            running.State == SchedulerJobState.Cancelled,
            "Runtime shutdown did not cancel its cooperative worker.");
    }

    private static void ShutdownIsFinal()
    {
        ConfigureScheduler();
        FixWorldScheduler.Initialize();
        Assert(FixWorldScheduler.WorkerCount == 1, "The scheduler did not start.");
        FixWorldScheduler.Shutdown();
        Assert(FixWorldScheduler.WorkerCount == 0, "Shutdown retained workers.");
        AssertThrows<ObjectDisposedException>(
            () => FixWorldScheduler.Initialize());
        AssertThrows<ObjectDisposedException>(
            () => FixWorldScheduler.Schedule(Job("after-stop", _ => 0)));
    }

    private static SchedulerJob<int> Job(
        string key,
        Func<CancellationToken, int> execute,
        SchedulerJobPriority priority = SchedulerJobPriority.Low,
        IReadOnlyList<ScheduledJobHandle> dependencies = null,
        long estimatedBytes = 0L,
        string concurrencyKey = null,
        int maxConcurrency = 0)
    {
        return new SchedulerJob<int>(
            key,
            key,
            SchedulerJobLifetime.Background,
            priority,
            SchedulerResourceClass.Cpu,
            execute,
            dependencies,
            estimatedBytes,
            concurrencyKey,
            maxConcurrency);
    }

    private static void ConfigureScheduler(
        int workers = 1,
        long byteCapacity = 16L * 1024L * 1024L)
    {
        Environment.SetEnvironmentVariable("FIXWORLD_WORKERS", workers.ToString());
        Environment.SetEnvironmentVariable(
            "FIXWORLD_SCHEDULER_IO",
            workers.ToString());
        Environment.SetEnvironmentVariable("FIXWORLD_SCHEDULER_QUEUE", "64");
        Environment.SetEnvironmentVariable(
            "FIXWORLD_SCHEDULER_BYTES",
            byteCapacity.ToString());
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
            "Expected exception " + typeof(TException).Name + ".");
    }

    private readonly struct FirstEvent
    {
        internal readonly int Value;

        internal FirstEvent(int value)
        {
            Value = value;
        }
    }

    private readonly struct SecondEvent
    {
        internal readonly int Value;

        internal SecondEvent(int value)
        {
            Value = value;
        }
    }
}
