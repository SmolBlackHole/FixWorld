using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using FixWorld.Profiling;

internal static class Program
{
    private const int DefaultIterations = 2_000_000;
    private const int ProducerCount = 8;
    private const int WarmupIterations = 100_000;

    private static int Main(string[] arguments)
    {
        try
        {
            int iterations = ParseIterations(arguments);
            Console.WriteLine(
                "FixWorld profiler harness: " +
                iterations.ToString("N0", CultureInfo.InvariantCulture) +
                " observations");

            Warmup();
            MeasureTimestamp(iterations);
            MeasureDisabled(iterations);
            MeasureSingleProducer(
                "inline atomic",
                ProfilerOptions.Inline,
                iterations);
            MeasureSingleProducer(
                "buffered shard",
                BufferedOptions(),
                iterations);
            MeasureTimedScope(
                "inline timed scope",
                ProfilerOptions.Inline,
                iterations);
            MeasureTimedScope(
                "buffered timed scope",
                BufferedOptions(),
                iterations);
            MeasureConcurrent(
                "inline atomic, 8 producers",
                ProfilerOptions.Inline,
                iterations);
            MeasureConcurrent(
                "buffered shard, 8 producers",
                BufferedOptions(),
                iterations);
            MeasureAlternatingBufferedProfilers(iterations);
            MeasurePublishedSnapshotReads(iterations);
            MeasureSnapshotPublication(Math.Max(1000, iterations / 1000));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static ProfilerOptions BufferedOptions() =>
        new(
            ProfileAggregationMode.Buffered,
            publishInterval: TimeSpan.FromMilliseconds(250));

    private static int ParseIterations(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return DefaultIterations;
        }

        if (arguments.Length != 1 ||
            !int.TryParse(
                arguments[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int iterations) ||
            iterations <= 0)
        {
            throw new ArgumentException(
                "Usage: FixWorld.Profiling.Benchmarks [positive iterations]");
        }

        return iterations;
    }

    private static void Warmup()
    {
        using (Profiler<int> profiler = new())
        {
            ProfileSlot<int> slot = profiler.GetSlot(0);
            for (int index = 0; index < WarmupIterations; index++)
            {
                slot.ObserveStopwatchTicks(1L);
            }
        }

        for (int index = 0; index < WarmupIterations; index++)
        {
            Stopwatch.GetTimestamp();
        }
    }

    private static void MeasureTimestamp(int iterations)
    {
        long sink = 0L;
        BenchmarkResult result = Measure(
            "Stopwatch.GetTimestamp",
            iterations,
            () =>
            {
                for (int index = 0; index < iterations; index++)
                {
                    sink ^= Stopwatch.GetTimestamp();
                }
            });
        GC.KeepAlive(sink);
        Print(result);
    }

    private static void MeasureDisabled(int iterations)
    {
        using Profiler<int> profiler = new(
                   options: new ProfilerOptions(
                       ProfileAggregationMode.Buffered,
                       enabled: false));
        ProfileSlot<int> slot = profiler.GetSlot(0);
        BenchmarkResult result = Measure(
            "disabled timed probe",
            iterations,
            () =>
            {
                for (int index = 0; index < iterations; index++)
                {
                    long startedAt = slot.StartTimestamp();
                    slot.StopTimestamp(startedAt);
                }
            });
        Print(result, slot.Snapshot());
    }

    private static void MeasureSingleProducer(
        string name,
        ProfilerOptions options,
        int iterations)
    {
        using Profiler<int> profiler = new(options: options);
        ProfileSlot<int> slot = profiler.GetSlot(0);
        BenchmarkResult result = Measure(
            name,
            iterations,
            () =>
            {
                for (int index = 0; index < iterations; index++)
                {
                    slot.ObserveStopwatchTicks(1L);
                }
            });
        Print(result, slot.Snapshot());
    }

    private static void MeasureTimedScope(
        string name,
        ProfilerOptions options,
        int iterations)
    {
        using Profiler<int> profiler = new(options: options);
        ProfileSlot<int> slot = profiler.GetSlot(0);
        BenchmarkResult result = Measure(
            name,
            iterations,
            () =>
            {
                for (int index = 0; index < iterations; index++)
                {
                    using (slot.Measure())
                    {
                    }
                }
            });
        Print(result, slot.Snapshot());
    }

    private static void MeasureConcurrent(
        string name,
        ProfilerOptions options,
        int iterations)
    {
        using Profiler<int> profiler = new(options: options);
        using ManualResetEventSlim start = new(false);
        ProfileSlot<int> slot = profiler.GetSlot(0);
        var producers = new Thread[ProducerCount];
        int perProducer = iterations / ProducerCount;
        int remainder = iterations % ProducerCount;
        for (int producer = 0; producer < producers.Length; producer++)
        {
            int count = perProducer + (producer < remainder ? 1 : 0);
            producers[producer] = new Thread(() =>
            {
                start.Wait();
                for (int index = 0; index < count; index++)
                {
                    slot.ObserveStopwatchTicks(1L);
                }
            });
            producers[producer].Start();
        }

        BenchmarkResult result = Measure(
            name,
            iterations,
            () =>
            {
                start.Set();
                foreach (Thread producer in producers)
                {
                    producer.Join();
                }
            });
        Print(result, slot.Snapshot());
    }

    private static void MeasurePublishedSnapshotReads(int iterations)
    {
        using Profiler<int> profiler = new();
        profiler.GetSlot(0).ObserveStopwatchTicks(1L);
        profiler.PublishSnapshot();
        ProfileSnapshot<int> sink = null;
        BenchmarkResult result = Measure(
            "published snapshot read",
            iterations,
            () =>
            {
                for (int index = 0; index < iterations; index++)
                {
                    sink = profiler.PublishedSnapshot;
                }
            });
        GC.KeepAlive(sink);
        Print(result);
    }

    private static void MeasureAlternatingBufferedProfilers(int iterations)
    {
        using Profiler<int> first = new(options: BufferedOptions());
        using Profiler<int> second = new(options: BufferedOptions());
        ProfileSlot<int> firstSlot = first.GetSlot(0);
        ProfileSlot<int> secondSlot = second.GetSlot(0);
        firstSlot.ObserveStopwatchTicks(1L);
        secondSlot.ObserveStopwatchTicks(1L);
        first.PublishSnapshot();
        second.PublishSnapshot();

        BenchmarkResult result = Measure(
            "buffered, alternating stores",
            iterations,
            () =>
            {
                for (int index = 0; index < iterations; index++)
                {
                    if ((index & 1) == 0)
                    {
                        firstSlot.ObserveStopwatchTicks(1L);
                    }
                    else
                    {
                        secondSlot.ObserveStopwatchTicks(1L);
                    }
                }
            });
        long accepted =
            firstSlot.Snapshot().Calls +
            secondSlot.Snapshot().Calls -
            2L;
        Print(result, accepted);
    }

    private static void MeasureSnapshotPublication(int iterations)
    {
        const int SlotCount = 64;
        using Profiler<int> profiler = new();
        for (int index = 0; index < SlotCount; index++)
        {
            profiler.GetSlot(index).ObserveStopwatchTicks(index + 1L);
        }

        BenchmarkResult result = Measure(
            "publish snapshot, 64 slots",
            iterations,
            () =>
            {
                for (int index = 0; index < iterations; index++)
                {
                    profiler.PublishSnapshot();
                }
            });
        Print(result);
    }

    private static BenchmarkResult Measure(
        string name,
        int operations,
        Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int gen0Before = GC.CollectionCount(0);
        Stopwatch stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        return new BenchmarkResult(
            name,
            operations,
            stopwatch.ElapsedTicks,
            GC.CollectionCount(0) - gen0Before);
    }

    private static void Print(
        BenchmarkResult result,
        ProfileMeasurement<int>? measurement = null)
    {
        double nanoseconds =
            result.ElapsedTicks * 1_000_000_000.0 /
            Stopwatch.Frequency /
            result.Operations;
        string suffix = measurement.HasValue
            ? ", accepted=" +
              measurement.Value.Calls.ToString(
                  "N0",
                  CultureInfo.InvariantCulture)
            : string.Empty;
        Console.WriteLine(
            result.Name.PadRight(30) +
            nanoseconds.ToString("F1", CultureInfo.InvariantCulture)
                .PadLeft(9) +
            " ns/op, gen0=" + result.Gen0Collections + suffix);
    }

    private static void Print(BenchmarkResult result, long accepted)
    {
        double nanoseconds =
            result.ElapsedTicks * 1_000_000_000.0 /
            Stopwatch.Frequency /
            result.Operations;
        Console.WriteLine(
            result.Name.PadRight(30) +
            nanoseconds.ToString("F1", CultureInfo.InvariantCulture)
                .PadLeft(9) +
            " ns/op, gen0=" + result.Gen0Collections +
            ", accepted=" +
            accepted.ToString("N0", CultureInfo.InvariantCulture));
    }

    private readonly struct BenchmarkResult
    {
        internal BenchmarkResult(
            string name,
            int operations,
            long elapsedTicks,
            int gen0Collections)
        {
            Name = name;
            Operations = operations;
            ElapsedTicks = elapsedTicks;
            Gen0Collections = gen0Collections;
        }

        internal string Name { get; }

        internal int Operations { get; }

        internal long ElapsedTicks { get; }

        internal int Gen0Collections { get; }
    }
}
