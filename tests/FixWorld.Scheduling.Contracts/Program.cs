using System;
using System.Collections.Generic;
using System.Threading;
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
            MainThreadDispatcherIsFifo();
            DispatcherShutdownIsAtomic();
            RuntimeShutdownCancelsCooperativeWorker();
            ShutdownIsFinal();
            Console.WriteLine(
                "Scheduler contracts passed; assertions=" + assertions + ".");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
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
        MainThreadDispatcher dispatcher = new MainThreadDispatcher(8);
        List<int> order = new List<int>();
        dispatcher.Enqueue("one", "one", () => order.Add(1));
        dispatcher.Enqueue("two", "two", () => order.Add(2));
        dispatcher.Enqueue("three", "three", () => order.Add(3));
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

    private static void DispatcherShutdownIsAtomic()
    {
        MainThreadDispatcher dispatcher = new MainThreadDispatcher(1);
        AssertThrows<ArgumentNullException>(
            () => dispatcher.Enqueue("invalid", "invalid", null));
        MainThreadActionHandle queued = dispatcher.Enqueue(
            "queued",
            "queued",
            () => { });
        dispatcher.CancelAll();
        Assert(
            queued.State == SchedulerJobState.Cancelled,
            "Dispatcher shutdown did not cancel an existing action.");
        AssertThrows<ObjectDisposedException>(
            () => dispatcher.Enqueue("late", "late", () => { }));
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
        FixWorldScheduler.Initialize();
        Assert(FixWorldScheduler.WorkerCount == 1, "The scheduler did not start.");
        FixWorldScheduler.Shutdown();
        Assert(FixWorldScheduler.WorkerCount == 0, "Shutdown retained workers.");
        AssertThrows<ObjectDisposedException>(FixWorldScheduler.Initialize);
        AssertThrows<ObjectDisposedException>(
            () => FixWorldScheduler.Schedule(Job("after-stop", _ => 0)));
    }

    private static SchedulerJob<int> Job(
        string key,
        Func<CancellationToken, int> execute)
    {
        return new SchedulerJob<int>(
            key,
            key,
            SchedulerJobLifetime.Background,
            SchedulerJobPriority.Low,
            SchedulerResourceClass.Cpu,
            execute);
    }

    private static void ConfigureScheduler()
    {
        Environment.SetEnvironmentVariable("FIXWORLD_WORKERS", "1");
        Environment.SetEnvironmentVariable("FIXWORLD_SCHEDULER_IO", "1");
        Environment.SetEnvironmentVariable("FIXWORLD_SCHEDULER_QUEUE", "64");
        Environment.SetEnvironmentVariable(
            "FIXWORLD_SCHEDULER_BYTES",
            (16L * 1024L * 1024L).ToString());
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
}
