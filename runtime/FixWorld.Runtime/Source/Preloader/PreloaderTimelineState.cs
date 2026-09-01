using System;
using System.Diagnostics;
using System.Globalization;

namespace FixWorld.Preloader
{
    internal static class PreloaderTimelineState
    {
        private static readonly object Sync = new object();

        private static int assembliesAtBootstrap;
        private static long bootstrapTimestamp;
        private static bool captured;

        internal static PreloaderTimelineSnapshot Capture()
        {
            lock (Sync)
            {
                if (!captured)
                {
                    bootstrapTimestamp = Stopwatch.GetTimestamp();
                    assembliesAtBootstrap =
                        AppDomain.CurrentDomain.GetAssemblies().Length;
                    captured = true;
                }

                return PreloaderTimelineContract.CaptureAtBootstrap(
                    bootstrapTimestamp,
                    assembliesAtBootstrap);
            }
        }

        internal static PreloaderTimelineSnapshot GetSnapshot()
        {
            return Capture();
        }

        internal static string Format(PreloaderTimelineSnapshot value)
        {
            if (!value.Active)
            {
                return "inactive";
            }

            return "doorstop=" + value.DoorstopVersion +
                   ", assemblyCSharpAtEntry=" + value.AssemblyCSharpAvailableAtEntry +
                   ", entryToAssemblyCSharpMs=" +
                   FormatMilliseconds(value.EntryToAssemblyCSharpMilliseconds) +
                   ", assemblyCSharpToFirstModAssemblyMs=" +
                   FormatMilliseconds(value.AssemblyCSharpToFirstModAssemblyMilliseconds) +
                   ", modAssemblyLoadMs=" +
                   FormatMilliseconds(value.ModAssemblyLoadMilliseconds) +
                   ", lastModAssemblyToBootstrapMs=" +
                   FormatMilliseconds(value.LastModAssemblyToBootstrapMilliseconds) +
                   ", entryToBootstrapMs=" +
                   FormatMilliseconds(value.EntryToBootstrapMilliseconds) +
                   ", modAssemblies=" + value.ModAssembliesLoaded +
                   ", assemblies=" + value.AssembliesAtEntry +
                   "->" + value.AssembliesAtBootstrap;
        }

        private static string FormatMilliseconds(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("F3", CultureInfo.InvariantCulture)
                : "n/a";
        }
    }
}
