using System;
using System.Collections.Generic;
using FixWorld.Diagnostics;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace FixWorld.Pathfinding
{
    // Main-thread observer of completed vanilla queries, never a gameplay provider.
    internal sealed class ShadowReachabilityComparison
    {
        private readonly RuntimeTelemetryStore telemetry;
        private readonly ShadowGridObserver grid;
        private int lastComparisonTick = int.MinValue;
        private bool mismatchWarningWritten;
        private bool manualComparisonInProgress;
        private System.WeakReference<Map> pendingMap;
        private IntVec3 pendingStart;
        private IntVec3 pendingDestination;
        private ShadowQueryUnavailableReason lastPendingReason;

        internal ShadowReachabilityComparison(
            RuntimeTelemetryStore telemetry,
            ShadowGridObserver grid)
        {
            this.telemetry = telemetry;
            this.grid = grid;
        }

        internal void Observe(
            Map map,
            IntVec3 start,
            LocalTargetInfo target,
            PathEndMode endMode,
            TraverseParms parms,
            bool actualResult)
        {
            if (!ShadowGridObserver.Enabled || !UnityData.IsInMainThread ||
                map == null || manualComparisonInProgress)
            {
                return;
            }

            long startedAt = 0L;
            bool sampled = false;
            bool recorded = false;
            try
            {
                if (!ReachabilityComparisonPolicy.IsCandidate(
                        start, target, endMode, parms))
                {
                    return;
                }

                telemetry.ObserveShadowGridQueryEligible(actualResult);
                TickManager ticks = Find.TickManager;
                if (ticks == null || ticks.TicksGame == lastComparisonTick)
                {
                    return;
                }

                lastComparisonTick = ticks.TicksGame;
                sampled = true;
                startedAt = telemetry.StartRuntimeHotpath(RuntimeHotpath.ShadowGridQuery);
                IntVec3 destination = target.Cell;
                ShadowQueryUnavailableReason reason = Query(
                    map, start, destination, out bool connected, out long generation);
                bool answered = reason == ShadowQueryUnavailableReason.None;
                if (answered)
                    telemetry.ObserveShadowGridQuery(true, connected);
                else
                    telemetry.ObserveShadowGridUnavailable(reason);
                recorded = true;
                if (answered)
                {
                    bool mismatched = connected != actualResult;
                    telemetry.ObserveShadowGridComparison(mismatched, actualResult);
                    if (mismatched && !mismatchWarningWritten)
                    {
                        mismatchWarningWritten = true;
                        Log.Warning(
                            "[FixWorld] Shadow reachability mismatch: map=" +
                            map.uniqueID + ", start=" + start + ", target=" +
                            destination + ", generation=" + generation +
                            ", vanilla=" + actualResult + ", shadow=" + connected +
                            ". Gameplay still uses the vanilla result.");
                    }
                }
            }
            catch
            {
                if (sampled && !recorded)
                {
                    try { telemetry.ObserveShadowGridUnavailable(ShadowQueryUnavailableReason.Exception); }
                    catch { }
                }
            }
            finally
            {
                if (startedAt != 0L)
                {
                    try { telemetry.StopRuntimeHotpath(RuntimeHotpath.ShadowGridQuery, startedAt); }
                    catch { }
                }
            }
        }

        // Selection only. The regular GatherData barrier executes the dev query.
        internal void CompareSelectedCells(Map map, IntVec3 start, IntVec3 destination)
        {
            if (!ShadowGridObserver.Enabled || !UnityData.IsInMainThread || map == null)
            {
                Log.Message("[FixWorld.ShadowTest] unavailable: observer disabled or no active map/main thread.");
                return;
            }

            TraverseParms parms = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);
            LocalTargetInfo target = new(destination);
            if (!ReachabilityComparisonPolicy.IsCandidate(start, target, PathEndMode.OnCell, parms))
            {
                Log.Message("[FixWorld.ShadowTest] Select two distinct valid floor cells.");
                return;
            }

            pendingStart = start;
            pendingDestination = destination;
            pendingMap = new System.WeakReference<Map>(map);
            lastPendingReason = ShadowQueryUnavailableReason.None;
            Log.Message("[FixWorld.ShadowTest] queued: map=" + map.uniqueID +
                ", start=" + start + ", target=" + destination +
                ". Resume simulation; comparison runs after regular path-data gathering. New selections replace this request.");
        }

        internal void CancelPending() => pendingMap = null;

        internal void AfterGatherData(Map map)
        {
            if (pendingMap == null || manualComparisonInProgress ||
                !UnityData.IsInMainThread)
                return;
            if (!ShadowGridObserver.Enabled || !pendingMap.TryGetTarget(out Map selectedMap))
            {
                CancelPending();
                return;
            }
            if (!ReferenceEquals(selectedMap, map)) return;

            IntVec3 start = pendingStart;
            IntVec3 destination = pendingDestination;
            TraverseParms parms = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);
            LocalTargetInfo target = new(destination);

            try
            {
                ShadowQueryUnavailableReason reason = Query(map, start, destination, out _, out _);
                if (reason != ShadowQueryUnavailableReason.None)
                {
                    bool transient = reason == ShadowQueryUnavailableReason.PendingCellDeltas ||
                        reason == ShadowQueryUnavailableReason.PendingRectDeltas ||
                        reason == ShadowQueryUnavailableReason.DirtyRegions ||
                        reason == ShadowQueryUnavailableReason.NotGathered ||
                        reason == ShadowQueryUnavailableReason.GridUnavailable;
                    if (!transient) CancelPending();
                    if (reason != lastPendingReason)
                    {
                        lastPendingReason = reason;
                        Log.Message("[FixWorld.ShadowTest] " +
                            (transient ? "waiting after gather: " : "cancelled: ") + reason);
                    }
                    return;
                }

                // Consume before calling Verse: recursive hooks cannot execute it twice.
                CancelPending();
                manualComparisonInProgress = true;
                bool vanilla = map.reachability.CanReach(start, target, PathEndMode.OnCell, parms);
                reason = Query(map, start, destination, out bool connected, out long generation);
                Log.Message("[FixWorld.ShadowTest] map=" + map.uniqueID +
                    ", start=" + start + ", target=" + destination +
                    ", generation=" + generation + ", vanilla=" + vanilla +
                    (reason == ShadowQueryUnavailableReason.None
                        ? ", shadow=" + connected + ", matched=" + (vanilla == connected)
                        : ", unavailable=" + reason));
            }
            catch (Exception exception)
            {
                CancelPending();
                Log.Warning("[FixWorld.ShadowTest] failed: " + exception);
            }
            finally
            {
                manualComparisonInProgress = false;
            }
        }

        private ShadowQueryUnavailableReason Query(
            Map map, IntVec3 start, IntVec3 destination,
            out bool connected, out long generation)
        {
            connected = false;
            generation = 0;
            if (!start.InBounds(map) || !destination.InBounds(map))
                return ShadowQueryUnavailableReason.OutOfBounds;
            if (!start.WalkableByNormal(map))
                return ShadowQueryUnavailableReason.StartBlocked;
            if (!destination.WalkableByNormal(map))
                return ShadowQueryUnavailableReason.TargetBlocked;
            ShadowQueryUnavailableReason reason = Freshness(map);
            if (reason != ShadowQueryUnavailableReason.None)
                return reason;
            return grid.TryQuery(map, start, destination, out connected, out generation)
                ? ShadowQueryUnavailableReason.None
                : ShadowQueryUnavailableReason.GridUnavailable;
        }

        private static ShadowQueryUnavailableReason Freshness(Map map)
        {
            PathFinderMapData data = map.pathFinder?.MapData;
            if (data == null || map.regionDirtyer == null)
                return ShadowQueryUnavailableReason.MissingMapData;
            if (!GatherState.Available)
                return ShadowQueryUnavailableReason.MissingFreshnessAccess;
            if (GatherState.LastTick(data) < 0)
                return ShadowQueryUnavailableReason.NotGathered;

            List<IntVec3> cells = GatherState.Cells(data);
            List<CellRect> rectangles = GatherState.Rectangles(data);
            if (cells == null || rectangles == null)
                return ShadowQueryUnavailableReason.MissingMapData;
            if (cells.Count != 0)
                return ShadowQueryUnavailableReason.PendingCellDeltas;
            if (rectangles.Count != 0)
                return ShadowQueryUnavailableReason.PendingRectDeltas;
            return map.regionDirtyer.AnyDirty
                ? ShadowQueryUnavailableReason.DirtyRegions
                : ShadowQueryUnavailableReason.None;
        }

        // Bind once. Missing game internals make the comparison unavailable;
        // they never cause a synchronous rebuild or another CanReach call.
        private static class GatherState
        {
            internal static readonly AccessTools.FieldRef<PathFinderMapData, int> LastTick;
            internal static readonly AccessTools.FieldRef<PathFinderMapData, List<IntVec3>> Cells;
            internal static readonly AccessTools.FieldRef<PathFinderMapData, List<CellRect>> Rectangles;
            internal static readonly bool Available;

            static GatherState()
            {
                try
                {
                    LastTick = AccessTools.FieldRefAccess<PathFinderMapData, int>("lastGatherTick");
                    Cells = AccessTools.FieldRefAccess<PathFinderMapData, List<IntVec3>>("cellDeltas");
                    Rectangles = AccessTools.FieldRefAccess<PathFinderMapData, List<CellRect>>("cellRectDeltas");
                    Available = LastTick != null && Cells != null && Rectangles != null;
                }
                catch (Exception exception)
                {
                    try { Log.Warning("[FixWorld] Shadow comparison freshness checks unavailable: " + exception.Message); }
                    catch { }
                }
            }
        }
    }
}
