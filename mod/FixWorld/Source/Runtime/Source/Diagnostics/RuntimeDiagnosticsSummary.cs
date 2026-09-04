using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FixWorld.Pathfinding;
using FixWorld.Profiling;
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

            var text = new StringBuilder(4096);
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
                    FormatStageResult(stage));
            }

            text.AppendLine();
            text.AppendLine("DDS cache");
            AppendDdsDetails(text, snapshot);

            text.AppendLine();
            text.AppendLine("Runtime");
            text.AppendLine(
                "  Scheduler: " + snapshot.Scheduler.WorkerCount +
                " workers, " + snapshot.Scheduler.PendingMainThreadActions +
                " main-thread actions queued");
            text.AppendLine("  Memory: " + FormatMemory(snapshot.Memory));

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

        internal static string FormatRuntimeDetails(
            string startupDiagnostics,
            RuntimeProfilingSnapshot profiling)
        {
            string startup = string.IsNullOrWhiteSpace(startupDiagnostics)
                ? "No completed startup diagnostics are available yet."
                : startupDiagnostics.TrimEnd();
            StringBuilder text = new(startup.Length + 2048);
            text.Append(startup);
            text.AppendLine();
            text.AppendLine();
            text.AppendLine("Hotpaths");
            text.AppendLine(
                "  Profiler: " + profiling.AggregationMode.ToString().ToLowerInvariant() +
                ", snapshot age: " +
                Milliseconds(profiling.Hotpaths.Age.TotalMilliseconds));

            bool hasMeasurements = false;
            for (int index = 0; index < profiling.Hotpaths.Count; index++)
            {
                ProfileMeasurement<RuntimeHotpath> measurement =
                    profiling.Hotpaths[index];
                if (measurement.Calls == 0L)
                {
                    continue;
                }

                hasMeasurements = true;
                text.AppendLine(
                    "  " + RuntimeHotpathCatalog.GetName(measurement.Key) +
                    ": " + measurement.Calls.ToString(
                        CultureInfo.InvariantCulture) +
                    " calls, " + Milliseconds(
                        measurement.TotalTime.TotalMilliseconds) +
                    " total, " + Milliseconds(
                        measurement.AverageTime.TotalMilliseconds) +
                    " average, " + Milliseconds(
                        measurement.MaximumTime.TotalMilliseconds) +
                    " max");
            }

            if (!hasMeasurements)
            {
                text.AppendLine("  No runtime measurements published yet.");
            }

            AppendPathfindingDetails(text, profiling.Pathfinding);
            AppendShadowGridDetails(text, profiling.ShadowGrid);
            return text.ToString().TrimEnd();
        }

        internal static string FormatRuntimeLog(
            string reason,
            RuntimeProfilingSnapshot profiling)
        {
            StringBuilder text = new(3072);
            text.Append("[FixWorld.Profile] reason=");
            text.Append(reason);
            text.Append("; mode=");
            text.Append(profiling.AggregationMode.ToString().ToLowerInvariant());
            text.Append("; snapshotAgeMs=");
            text.Append(profiling.Hotpaths.Age.TotalMilliseconds.ToString(
                "F1",
                CultureInfo.InvariantCulture));
            text.Append("; hotpaths(calls,totalMs,avgMs,maxMs)=");

            bool first = true;
            for (int index = 0; index < profiling.Hotpaths.Count; index++)
            {
                ProfileMeasurement<RuntimeHotpath> measurement =
                    profiling.Hotpaths[index];
                if (measurement.Calls == 0L)
                {
                    continue;
                }

                if (!first)
                {
                    text.Append('|');
                }

                first = false;
                text.Append(measurement.Key);
                text.Append(':');
                text.Append(measurement.Calls.ToString(
                    CultureInfo.InvariantCulture));
                text.Append(',');
                text.Append(measurement.TotalTime.TotalMilliseconds.ToString(
                    "F1",
                    CultureInfo.InvariantCulture));
                text.Append(',');
                text.Append(measurement.AverageTime.TotalMilliseconds.ToString(
                    "F3",
                    CultureInfo.InvariantCulture));
                text.Append(',');
                text.Append(measurement.MaximumTime.TotalMilliseconds.ToString(
                    "F1",
                    CultureInfo.InvariantCulture));
            }

            RuntimePathfindingSnapshot path = profiling.Pathfinding;
            text.Append("; path=batches:");
            text.Append(path.Batches.ToString(CultureInfo.InvariantCulture));
            text.Append(",requests:");
            text.Append(path.Requests.ToString(CultureInfo.InvariantCulture));
            text.Append(",maxBatch:");
            text.Append(path.MaximumBatchSize.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",queueTicks:");
            text.Append(path.TotalQueueDelayTicks.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",maxQueueTicks:");
            text.Append(path.MaximumQueueDelayTicks.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",dataUpdates:");
            text.Append(path.DataUpdates.ToString(CultureInfo.InvariantCulture));
            text.Append(",dirtyCells:");
            text.Append(path.DirtyCells.ToString(CultureInfo.InvariantCulture));
            text.Append(",maxDirtyCells:");
            text.Append(path.MaximumDirtyCells.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",gridJobs:");
            text.Append(path.GridJobsCreated.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",reachHits:");
            text.Append(path.ReachabilityCacheHits.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",reachMisses:");
            text.Append(path.ReachabilityCacheMisses.ToString(
                CultureInfo.InvariantCulture));
            RuntimePathRequestSnapshot demand = path.RequestDemand;
            text.Append("; demand=observations:");
            text.Append(demand.Observations.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",origins:");
            text.Append(FormatPawnCategories(demand.PawnCategories));
            text.Append(",traversal:");
            text.Append(FormatTraversalModes(demand.TraversalModes));
            text.Append(",endModes:");
            text.Append(FormatEndModes(demand.EndModes));
            text.Append(",targets:");
            text.Append(FormatTargetKinds(demand.TargetKinds));
            text.Append(",distance:");
            text.Append(FormatDistanceBuckets(demand.DistanceBuckets));
            text.Append(",locality:");
            text.Append(FormatLocalities(demand.Localities));
            text.Append(",repeated600:");
            text.Append(demand.RepeatedTargets.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",targetCollisions:");
            text.Append(demand.TargetTrackerCollisions.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",constraints:");
            text.Append(FormatConstraints(demand.Constraints));
            RuntimeSpatialSnapshot spatial = path.Spatial;
            text.Append("; spatial=expandedVisits:");
            text.Append(spatial.ExpandedCellVisits.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",uniqueExpanded:");
            text.Append(spatial.UniqueExpandedCells.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",chunks8:");
            text.Append(spatial.Chunks8.ToString(CultureInfo.InvariantCulture));
            text.Append(",chunks16:");
            text.Append(spatial.Chunks16.ToString(CultureInfo.InvariantCulture));
            text.Append(",chunks32:");
            text.Append(spatial.Chunks32.ToString(CultureInfo.InvariantCulture));
            RuntimeShadowGridSnapshot shadow = profiling.ShadowGrid;
            text.Append("; shadow=full:");
            text.Append(shadow.FullUpdates.ToString(CultureInfo.InvariantCulture));
            text.Append(",incremental:");
            text.Append(shadow.IncrementalUpdates.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",sampled:");
            text.Append(shadow.SampledCells.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",changed:");
            text.Append(shadow.ChangedCells.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",rebuiltLeaves:");
            text.Append(shadow.RebuiltLeaves.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",changedLeaves:");
            text.Append(shadow.ChangedLeaves.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",rebuiltRegions:");
            text.Append(shadow.RebuiltRegions.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",changedRegions:");
            text.Append(shadow.ChangedRegions.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",rebuiltSuperChunks:");
            text.Append(shadow.RebuiltSuperChunks.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",changedSuperChunks:");
            text.Append(shadow.ChangedSuperChunks.ToString(
                CultureInfo.InvariantCulture));
            text.Append(",failures:");
            text.Append(shadow.Failures.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
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

        }

        private static void AppendPathfindingDetails(
            StringBuilder text,
            RuntimePathfindingSnapshot pathfinding)
        {
            text.AppendLine();
            text.AppendLine("Pathfinding");
            double averageBatch = pathfinding.Batches == 0L
                ? 0.0
                : pathfinding.Requests / (double)pathfinding.Batches;
            double averageQueueDelay = pathfinding.Requests == 0L
                ? 0.0
                : pathfinding.TotalQueueDelayTicks /
                  (double)pathfinding.Requests;
            text.AppendLine(
                "  Batches: " + pathfinding.Batches.ToString(
                    CultureInfo.InvariantCulture) +
                ", requests: " + pathfinding.Requests.ToString(
                    CultureInfo.InvariantCulture) +
                ", average size: " + averageBatch.ToString(
                    "F2",
                    CultureInfo.InvariantCulture) +
                ", maximum: " + pathfinding.MaximumBatchSize.ToString(
                    CultureInfo.InvariantCulture));
            text.AppendLine(
                "  Queue delay: " + averageQueueDelay.ToString(
                    "F2",
                    CultureInfo.InvariantCulture) +
                " ticks average, " +
                pathfinding.MaximumQueueDelayTicks.ToString(
                    CultureInfo.InvariantCulture) +
                " ticks maximum");
            double averageDirtyCells = pathfinding.DataUpdates == 0L
                ? 0.0
                : pathfinding.DirtyCells / (double)pathfinding.DataUpdates;
            text.AppendLine(
                "  Dirty cells: " + averageDirtyCells.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) +
                " average, " + pathfinding.MaximumDirtyCells.ToString(
                    CultureInfo.InvariantCulture) +
                " maximum across " + pathfinding.DataUpdates.ToString(
                    CultureInfo.InvariantCulture) +
                " data updates");
            text.AppendLine(
                "  Grid jobs created: " +
                pathfinding.GridJobsCreated.ToString(
                    CultureInfo.InvariantCulture));

            RuntimePathRequestSnapshot demand = pathfinding.RequestDemand;
            text.AppendLine(
                "  Request origins: " +
                FormatPawnCategories(demand.PawnCategories));
            text.AppendLine(
                "  Traversal modes: " +
                FormatTraversalModes(demand.TraversalModes));
            text.AppendLine(
                "  End modes: " + FormatEndModes(demand.EndModes));
            text.AppendLine(
                "  Targets: " + FormatTargetKinds(demand.TargetKinds));
            text.AppendLine(
                "  Request locality: " +
                FormatLocalities(demand.Localities));
            double averageDistance = demand.Observations == 0L
                ? 0.0
                : demand.TotalDistance / (double)demand.Observations;
            double repeatedTargetRate = demand.Observations == 0L
                ? 0.0
                : demand.RepeatedTargets * 100.0 / demand.Observations;
            text.AppendLine(
                "  Request distance: " + averageDistance.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) +
                " cells average, " + demand.MaximumDistance.ToString(
                    CultureInfo.InvariantCulture) +
                " maximum; " +
                FormatDistanceBuckets(demand.DistanceBuckets));
            text.AppendLine(
                "  Repeated targets: " + demand.RepeatedTargets.ToString(
                    CultureInfo.InvariantCulture) +
                " within 600 ticks, " + repeatedTargetRate.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) +
                "% of created requests, " +
                demand.TargetTrackerCollisions.ToString(
                    CultureInfo.InvariantCulture) +
                " tracker collisions");
            text.AppendLine(
                "  Request constraints: " +
                FormatConstraints(demand.Constraints));

            RuntimeSpatialSnapshot spatial = pathfinding.Spatial;
            double uniqueRatio = spatial.ExpandedCellVisits == 0L
                ? 0.0
                : spatial.UniqueExpandedCells * 100.0 /
                  spatial.ExpandedCellVisits;
            text.AppendLine(
                "  Connectivity expansion: " +
                spatial.ExpandedCellVisits.ToString(
                    CultureInfo.InvariantCulture) +
                " cell visits, " + spatial.UniqueExpandedCells.ToString(
                    CultureInfo.InvariantCulture) +
                " unique, " + uniqueRatio.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) +
                "% retained after deduplication");
            text.AppendLine(
                "  Chunks per update: " +
                FormatChunkMeasurement(
                    spatial.Chunks8,
                    spatial.MaximumChunks8,
                    pathfinding.DataUpdates,
                    8) + ", " +
                FormatChunkMeasurement(
                    spatial.Chunks16,
                    spatial.MaximumChunks16,
                    pathfinding.DataUpdates,
                    16) + ", " +
                FormatChunkMeasurement(
                    spatial.Chunks32,
                    spatial.MaximumChunks32,
                    pathfinding.DataUpdates,
                    32));

            long cacheLookups = pathfinding.ReachabilityCacheHits +
                                pathfinding.ReachabilityCacheMisses;
            double cacheHitRate = cacheLookups == 0L
                ? 0.0
                : pathfinding.ReachabilityCacheHits * 100.0 / cacheLookups;
            text.AppendLine(
                "  Reachability cache: " +
                pathfinding.ReachabilityCacheHits.ToString(
                    CultureInfo.InvariantCulture) +
                " hits, " + pathfinding.ReachabilityCacheMisses.ToString(
                    CultureInfo.InvariantCulture) +
                " misses, " + cacheHitRate.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) +
                "% hit rate");
        }

        private static void AppendShadowGridDetails(
            StringBuilder text,
            RuntimeShadowGridSnapshot shadow)
        {
            text.AppendLine();
            text.AppendLine("Shadow grid");
            text.AppendLine(
                "  Configured: " +
                (ShadowGridObserver.Enabled ? "enabled test" : "disabled") +
                ", binary/cardinal observer only, gameplay unchanged");
            text.AppendLine(
                "  Updates: " + shadow.FullUpdates.ToString(
                    CultureInfo.InvariantCulture) +
                " full, " + shadow.IncrementalUpdates.ToString(
                    CultureInfo.InvariantCulture) + " incremental");
            text.AppendLine(
                "  Cells: " + shadow.SampledCells.ToString(
                    CultureInfo.InvariantCulture) + " sampled, " +
                shadow.ChangedCells.ToString(CultureInfo.InvariantCulture) +
                " changed");
            text.AppendLine(
                "  Leaves: " + shadow.RebuiltLeaves.ToString(
                    CultureInfo.InvariantCulture) + " rebuilt, " +
                shadow.ChangedLeaves.ToString(CultureInfo.InvariantCulture) +
                " changed");
            text.AppendLine(
                "  Regions: " + shadow.RebuiltRegions.ToString(
                    CultureInfo.InvariantCulture) + " rebuilt, " +
                shadow.ChangedRegions.ToString(CultureInfo.InvariantCulture) +
                " changed");
            text.AppendLine(
                "  Super-chunks: " + shadow.RebuiltSuperChunks.ToString(
                    CultureInfo.InvariantCulture) + " rebuilt, " +
                shadow.ChangedSuperChunks.ToString(
                    CultureInfo.InvariantCulture) + " changed");
            text.AppendLine(
                "  Timing: nested Rebuild is included in full/incremental " +
                "update timing");
            text.AppendLine(
                "  Failures: " + shadow.Failures.ToString(
                    CultureInfo.InvariantCulture));
        }

        private static string FormatPawnCategories(long[] counts) =>
            FormatCounts(
                counts,
                index => PathRequestCatalog.GetName(
                    (PathRequestPawnCategory)index));

        private static string FormatTraversalModes(long[] counts) =>
            FormatCounts(
                counts,
                index => PathRequestCatalog.GetName(
                    (PathRequestTraversalMode)index));

        private static string FormatEndModes(long[] counts) =>
            FormatCounts(
                counts,
                index => PathRequestCatalog.GetName(
                    (PathRequestEndMode)index));

        private static string FormatTargetKinds(long[] counts) =>
            FormatCounts(
                counts,
                index => PathRequestCatalog.GetName(
                    (PathRequestTargetKind)index));

        private static string FormatDistanceBuckets(long[] counts) =>
            FormatCounts(
                counts,
                index => PathRequestCatalog.GetName(
                    (PathRequestDistanceBucket)index));

        private static string FormatLocalities(long[] counts) =>
            FormatCounts(
                counts,
                index => PathRequestCatalog.GetName(
                    (PathRequestLocality)index));

        private static string FormatConstraints(long[] counts) =>
            FormatCounts(
                counts,
                index => PathRequestCatalog.GetName(
                    (PathRequestConstraint)(1 << index)),
                includeZero: false);

        private static string FormatCounts(
            long[] counts,
            Func<int, string> name,
            bool includeZero = true)
        {
            var text = new StringBuilder(192);
            for (int index = 0; index < counts.Length; index++)
            {
                if (!includeZero && counts[index] == 0L)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append(", ");
                }

                text.Append(name(index));
                text.Append(' ');
                text.Append(counts[index].ToString(CultureInfo.InvariantCulture));
            }

            return text.Length == 0 ? "none" : text.ToString();
        }

        private static string FormatChunkMeasurement(
            long total,
            long maximum,
            long updates,
            int size)
        {
            double average = updates == 0L ? 0.0 : total / (double)updates;
            return size.ToString(CultureInfo.InvariantCulture) + "x" +
                   size.ToString(CultureInfo.InvariantCulture) + " " +
                   average.ToString("F1", CultureInfo.InvariantCulture) +
                   " average/" + maximum.ToString(
                       CultureInfo.InvariantCulture) + " max";
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

        private static string FormatStageResult(
            PlayDataStageMeasurement stage)
        {
            if (stage.Calls == 1L && stage.Failures == 0L)
            {
                return string.Empty;
            }

            return ", " + stage.Calls.ToString(CultureInfo.InvariantCulture) +
                   " calls, " +
                   stage.Failures.ToString(CultureInfo.InvariantCulture) +
                   " failures";
        }

        private static string FormatDds(RuntimeDiagnosticsSnapshot snapshot)
        {
            if (!snapshot.DdsCache.Enabled)
            {
                return "disabled";
            }

            string summary = snapshot.DdsCache.Hits + " hits/" +
                             snapshot.DdsCache.Misses + " misses";
            return summary;
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
