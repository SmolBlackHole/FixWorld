using System;
using System.Globalization;

namespace FixWorld.Scheduling
{
    internal static class RuntimeSchedulerSettings
    {
        private const string WorkerVariable = "FIXWORLD_WORKERS";
        private const string IoVariable = "FIXWORLD_SCHEDULER_IO";
        private const string QueueVariable = "FIXWORLD_SCHEDULER_QUEUE";
        private const string ByteVariable = "FIXWORLD_SCHEDULER_BYTES";

        internal static JobSchedulerOptions Create()
        {
            int processorCount = Math.Max(1, Environment.ProcessorCount);
            int workers = ReadInt(
                WorkerVariable,
                Math.Max(1, processorCount / 2),
                1,
                processorCount);
            int io = ReadInt(
                IoVariable,
                Math.Max(1, workers / 2),
                1,
                workers);
            int queue = ReadInt(QueueVariable, 4096, workers, 65536);
            long bytes = ReadLong(
                ByteVariable,
                512L * 1024L * 1024L,
                16L * 1024L * 1024L,
                64L * 1024L * 1024L * 1024L);
            return new JobSchedulerOptions(
                workers,
                io,
                queue,
                bytes,
                "FixWorld Worker");
        }

        private static int ReadInt(
            string name,
            int fallback,
            int minimum,
            int maximum)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            if (!int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value) ||
                value < minimum ||
                value > maximum)
            {
                throw new InvalidOperationException(
                    "Invalid " + name + " value: " + text + ".");
            }

            return value;
        }

        private static long ReadLong(
            string name,
            long fallback,
            long minimum,
            long maximum)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            if (!long.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long value) ||
                value < minimum ||
                value > maximum)
            {
                throw new InvalidOperationException(
                    "Invalid " + name + " value: " + text + ".");
            }

            return value;
        }
    }
}
