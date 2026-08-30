using System;

namespace RimWorldOptim.Poc.Profiling
{
    internal static class ProfilerRegistry
    {
        internal static bool IsEnabled(string environmentVariable)
        {
            return string.Equals(
                Environment.GetEnvironmentVariable(environmentVariable),
                "1",
                StringComparison.Ordinal);
        }

        internal static void WriteSummaries()
        {
            FileDiscoveryProfiler.WriteSummary();
            TexturePathProfiler.WriteSummary();
            TextureLoaderProfiler.WriteSummary();
        }

        internal static double ToMilliseconds(long stopwatchTicks)
        {
            return stopwatchTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }
    }
}
