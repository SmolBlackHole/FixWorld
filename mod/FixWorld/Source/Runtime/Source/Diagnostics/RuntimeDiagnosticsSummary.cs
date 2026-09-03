using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FixWorld.Textures;

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

            return "[FixWorld.Runtime] Startup diagnostics v" +
                   snapshot.SchemaVersion +
                   "; source=" + CompactLabel(snapshot.CompletionSource) +
                   "; playData=" + Milliseconds(
                       snapshot.Loading.ObservedMilliseconds) +
                   "; stages=" + FormatStageHotpaths(snapshot.Loading.Stages) +
                   "; dds=" + FormatDds(snapshot) +
                   "; scheduler=" + snapshot.Scheduler.WorkerCount +
                   " workers/" + snapshot.Scheduler.PendingMainThreadActions +
                   " main queued" +
                   "; memory=" + FormatMemory(snapshot.Memory) + ".";
        }

        internal static string FormatDetails(RuntimeDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            StringBuilder text = new StringBuilder(4096);
            text.AppendLine("Startup");
            text.AppendLine("  Completed: " + snapshot.CompletedUtc);
            text.AppendLine("  Source: " + snapshot.CompletionSource);
            text.AppendLine(
                "  Play data: " + Milliseconds(
                    snapshot.Loading.ObservedMilliseconds));

            text.AppendLine();
            text.AppendLine("Preloader");
            text.AppendLine(
                "  Active: " + snapshot.Preloader.Active +
                ", Doorstop: " + (snapshot.Preloader.DoorstopVersion ?? "unknown"));
            text.AppendLine(
                "  Entry to Runtime: " + OptionalMilliseconds(
                    snapshot.Preloader.EntryToBootstrapMilliseconds));
            text.AppendLine(
                "  Observed mod assemblies: " +
                snapshot.Preloader.ModAssembliesLoaded +
                ", load span: " + OptionalMilliseconds(
                    snapshot.Preloader.ModAssemblyLoadMilliseconds));

            text.AppendLine();
            text.AppendLine("Stages");
            foreach (PlayDataStageMeasurement stage in snapshot.Loading.Stages)
            {
                text.AppendLine(
                    "  " + stage.Number.ToString("00", CultureInfo.InvariantCulture) +
                    "  " + stage.Name + ": " +
                    Milliseconds(stage.ElapsedMilliseconds) +
                    " (" + FormatStageExecution(stage) + ")");
            }

            text.AppendLine();
            text.AppendLine("DDS and textures");
            AppendDdsDetails(text, snapshot);

            text.AppendLine();
            text.AppendLine("Runtime");
            text.AppendLine(
                "  Scheduler: " + snapshot.Scheduler.WorkerCount +
                " workers, " + snapshot.Scheduler.PendingMainThreadActions +
                " main-thread actions queued");
            text.AppendLine("  Memory: " + FormatMemory(snapshot.Memory));
            text.AppendLine(
                "  Detailed texture capture: " +
                snapshot.DetailedCaptureEnabled);

            text.AppendLine();
            text.AppendLine("Issues");
            bool hasIssues = false;
            if (snapshot.DdsCache.Failed > 0)
            {
                text.AppendLine("  DDS build failures: " + snapshot.DdsCache.Failed);
                hasIssues = true;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.DdsReadAhead.Error))
            {
                text.AppendLine("  DDS read-ahead: " + snapshot.DdsReadAhead.Error);
                hasIssues = true;
            }

            if (!hasIssues)
            {
                text.AppendLine("  None reported.");
            }

            return text.ToString().TrimEnd();
        }

        private static void AppendDdsDetails(
            StringBuilder text,
            RuntimeDiagnosticsSnapshot snapshot)
        {
            TextureDdsCacheSnapshot cache = snapshot.DdsCache;
            if (!cache.Enabled)
            {
                text.AppendLine("  Cache: disabled");
            }
            else
            {
                text.AppendLine(
                    "  Cache: " + cache.Hits + " hits, " + cache.Misses +
                    " misses, " + Mebibytes(cache.CacheBytes) + " / " +
                    Mebibytes(cache.MaxCacheBytes) + " MiB");
                text.AppendLine(
                    "  Maintenance: " + cache.Created + " created, " +
                    cache.Invalidated + " invalidated, " + cache.Failed +
                    " failed, " + Milliseconds(cache.BuildMilliseconds));
                text.AppendLine(
                    "  Workers: " + cache.WorkerCount + ", " +
                    cache.WorkerPreparedMods + " mods prepared, " +
                    cache.WorkerAppliedMods + " applied, " +
                    cache.WorkerFallbackMods + " fallbacks");
            }

            DdsReadAheadSnapshot readAhead = snapshot.DdsReadAhead;
            text.AppendLine(
                "  Read-ahead: " + (readAhead.Status ?? "inactive") +
                ", " + readAhead.FilesRead + " files, " +
                Mebibytes(readAhead.BytesRead) + " MiB, " +
                Milliseconds(readAhead.ElapsedMilliseconds));

            if (snapshot.DetailedCaptureEnabled)
            {
                TextureProbeSnapshot textures = snapshot.Textures;
                text.AppendLine(
                    "  Texture probe: " + textures.Files + " files, " +
                    Mebibytes(textures.Bytes) + " MiB, " +
                    Milliseconds(textures.TotalMilliseconds));
                text.AppendLine(
                    "  DDS reads: " + textures.DdsFiles + " files, " +
                    Mebibytes(textures.DdsBytes) + " MiB, " +
                    Milliseconds(textures.DdsMilliseconds));
            }
        }

        private static string FormatStageHotpaths(
            IReadOnlyList<PlayDataStageMeasurement> stages)
        {
            return string.Join(
                "|",
                stages
                    .OrderByDescending(item => item.ElapsedMilliseconds)
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .Take(HotpathCount)
                    .Select(item => CompactLabel(item.Name) + "=" +
                                    Milliseconds(item.ElapsedMilliseconds)));
        }

        private static string FormatStageExecution(
            PlayDataStageMeasurement stage)
        {
            string thread = stage.Thread + " #" +
                            stage.ManagedThreadId.ToString(
                                CultureInfo.InvariantCulture);
            if (!stage.ResourceMetricsAvailable)
            {
                return thread + ", resources unavailable";
            }

            return thread +
                   ", CPU " + stage.CpuCoreEquivalent.ToString(
                       "0.00",
                       CultureInfo.InvariantCulture) + "x" +
                   ", heap " + SignedMebibytes(
                       stage.ManagedHeapDeltaBytes) +
                   ", working set " + SignedMebibytes(
                       stage.WorkingSetDeltaBytes) +
                   ", GC " + stage.GenerationZeroCollections + "/" +
                   stage.GenerationOneCollections + "/" +
                   stage.GenerationTwoCollections;
        }

        private static string SignedMebibytes(long bytes)
        {
            double mebibytes = bytes / (1024.0 * 1024.0);
            return (mebibytes > 0.0 ? "+" : string.Empty) +
                   mebibytes.ToString("0.0", CultureInfo.InvariantCulture) +
                   " MiB";
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

        private static string OptionalMilliseconds(double? value)
        {
            return value.HasValue ? Milliseconds(value.Value) : "unavailable";
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
