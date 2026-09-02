using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FixWorld.Loading;
using FixWorld.PlayData;

namespace FixWorld.Diagnostics
{
    internal static class RuntimeDiagnosticsSummary
    {
        private const int HotpathCount = 3;
        private const int MaximumLabelLength = 72;

        internal static string Format(RuntimeDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            IReadOnlyList<DeferredWorkMeasurement> deferred =
                snapshot.DeferredWork.Measurements;
            long deferredCalls = deferred.Sum(item => item.Calls);
            long deferredFailures = deferred.Sum(item => item.Failures);
            double deferredMilliseconds = deferred.Sum(
                item => item.TotalTime.TotalMilliseconds);

            return "[FixWorld.Runtime] Startup diagnostics v" +
                   snapshot.SchemaVersion +
                   "; source=" + CompactLabel(snapshot.CompletionSource) +
                   "; playData=" + Milliseconds(
                       snapshot.Loading.ObservedMilliseconds) +
                   "; stages=" + FormatStageHotpaths(snapshot.Loading.Steps) +
                   "; deferred=" + deferredCalls + " calls/" +
                   Milliseconds(deferredMilliseconds) + "/" +
                   deferredFailures + " failed, top=" +
                   FormatDeferredHotpaths(deferred) +
                   "; dds=" + FormatDds(snapshot) +
                   "; scheduler=" + snapshot.Scheduler.WorkerCount +
                   " workers/" + snapshot.Scheduler.PendingMainThreadActions +
                   " main queued" +
                   "; memory=" + FormatMemory(snapshot.Memory) + ".";
        }

        private static string FormatStageHotpaths(
            IReadOnlyList<LoadingStepMeasurement> steps)
        {
            return string.Join(
                "|",
                steps
                    .OrderByDescending(item => item.ExclusiveMilliseconds)
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .Take(HotpathCount)
                    .Select(item => CompactLabel(item.Name) + "=" +
                                    Milliseconds(item.ExclusiveMilliseconds)));
        }

        private static string FormatDeferredHotpaths(
            IReadOnlyList<DeferredWorkMeasurement> measurements)
        {
            if (measurements.Count == 0)
            {
                return "none";
            }

            return string.Join(
                "|",
                measurements
                    .OrderByDescending(item => item.TotalTime)
                    .ThenBy(item => item.Owner, StringComparer.Ordinal)
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .Take(HotpathCount)
                    .Select(item => CompactLabel(item.Owner + ":" + item.Name) +
                                    "=" + Milliseconds(
                                        item.TotalTime.TotalMilliseconds)));
        }

        private static string FormatDds(RuntimeDiagnosticsSnapshot snapshot)
        {
            if (!snapshot.DdsCache.Enabled)
            {
                return "disabled";
            }

            string summary = snapshot.DdsCache.Hits + " hits/" +
                             snapshot.DdsCache.Misses + " misses";
            return snapshot.DetailedCaptureEnabled
                ? summary + "/" +
                  Milliseconds(snapshot.Textures.DdsMilliseconds) + " read"
                : summary;
        }

        private static string FormatMemory(SystemMemorySnapshot memory)
        {
            return memory.Available
                ? "process=" + Mebibytes(memory.ProcessBytes) +
                  "MiB/free=" + Mebibytes(memory.FreePhysicalBytes) + "MiB"
                : "unavailable";
        }

        private static string Milliseconds(double value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture) + "ms";
        }

        private static string Mebibytes(long bytes)
        {
            return (bytes / (1024.0 * 1024.0)).ToString(
                "F0",
                CultureInfo.InvariantCulture);
        }

        private static string CompactLabel(string value)
        {
            string compact = string.IsNullOrWhiteSpace(value)
                ? "unknown"
                : value.Replace(';', '/').Replace('|', '/').Trim();
            return compact.Length <= MaximumLabelLength
                ? compact
                : compact.Substring(0, MaximumLabelLength - 3) + "...";
        }
    }
}
