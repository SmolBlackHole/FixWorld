using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;
using Verse;

namespace RimWorldOptim.Poc.Profiling
{
    internal static class FileDiscoveryProfiler
    {
        private const string EnabledEnvironmentVariable = "RIMWORLDOPTIM_PROFILE_FILE_DISCOVERY";

        private static readonly bool Enabled = ProfilerRegistry.IsEnabled(EnabledEnvironmentVariable);

        private static long callCount;
        private static long fileCount;
        private static long totalTicks;
        private static long textureCallCount;
        private static long textureFileCount;
        private static long textureTicks;

        internal static long Begin()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void End(
            long startedAt,
            string contentPath,
            Dictionary<string, FileInfo> files)
        {
            if (startedAt == 0L)
            {
                return;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            int filesFound = files?.Count ?? 0;
            Interlocked.Increment(ref callCount);
            Interlocked.Add(ref fileCount, filesFound);
            Interlocked.Add(ref totalTicks, elapsedTicks);

            if (string.Equals(contentPath, GenFilePaths.ContentPath<Texture2D>(), StringComparison.Ordinal))
            {
                Interlocked.Increment(ref textureCallCount);
                Interlocked.Add(ref textureFileCount, filesFound);
                Interlocked.Add(ref textureTicks, elapsedTicks);
            }
        }

        internal static void WriteSummary()
        {
            if (!Enabled)
            {
                return;
            }

            Log.Message(string.Format(
                CultureInfo.InvariantCulture,
                "[RimWorldOptim.Poc] File discovery profile: calls={0}; files={1}; totalMs={2:0.###}; textureCalls={3}; textureFiles={4}; textureMs={5:0.###}",
                Interlocked.Read(ref callCount),
                Interlocked.Read(ref fileCount),
                ProfilerRegistry.ToMilliseconds(Interlocked.Read(ref totalTicks)),
                Interlocked.Read(ref textureCallCount),
                Interlocked.Read(ref textureFileCount),
                ProfilerRegistry.ToMilliseconds(Interlocked.Read(ref textureTicks))));
        }
    }
}
