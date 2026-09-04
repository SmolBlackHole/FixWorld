using System;
using FixWorld.Profiling;

namespace FixWorld.Diagnostics
{
    internal enum RuntimeHotpath
    {
        Tick,
        MapPreTick,
        MapPostTick,
        PathFinderTick,
        PathFinderPushRequest,
        PathFinderFindPathNow,
        PathFinderGatherMapData,
        PathFinderSourceCosts,
        PathFinderSourceAreas,
        PathFinderSourcePerceptual,
        PathFinderSourceConnectivity,
        PathFinderSourceWater,
        PathFinderSourceFences,
        PathFinderSourceBuildings,
        PathFinderSourceFactions,
        PathFinderSourceFog,
        PathFinderSourcePersistentDanger,
        PathFinderSourceDarkness,
        PathFinderJobBarrier,
        PathFinderGridScheduling,
        PathFinderPathScheduling,
        ReachabilityCanReach,
        ReachabilityCacheLookup
    }

    internal static class RuntimeHotpathCatalog
    {
        internal const int Count = 23;

        internal static string GetName(RuntimeHotpath hotpath)
        {
            switch (hotpath)
            {
                case RuntimeHotpath.Tick:
                    return "TickManager.DoSingleTick";
                case RuntimeHotpath.MapPreTick:
                    return "Map.MapPreTick";
                case RuntimeHotpath.MapPostTick:
                    return "Map.MapPostTick";
                case RuntimeHotpath.PathFinderTick:
                    return "PathFinder.PathFinderTick";
                case RuntimeHotpath.PathFinderPushRequest:
                    return "PathFinder.PushRequest";
                case RuntimeHotpath.PathFinderFindPathNow:
                    return "PathFinder.FindPathNow";
                case RuntimeHotpath.PathFinderGatherMapData:
                    return "PathFinderMapData.GatherData";
                case RuntimeHotpath.PathFinderSourceCosts:
                    return "Path data: movement costs";
                case RuntimeHotpath.PathFinderSourceAreas:
                    return "Path data: areas";
                case RuntimeHotpath.PathFinderSourcePerceptual:
                    return "Path data: perceptual costs";
                case RuntimeHotpath.PathFinderSourceConnectivity:
                    return "Path data: connectivity";
                case RuntimeHotpath.PathFinderSourceWater:
                    return "Path data: water";
                case RuntimeHotpath.PathFinderSourceFences:
                    return "Path data: fences";
                case RuntimeHotpath.PathFinderSourceBuildings:
                    return "Path data: buildings";
                case RuntimeHotpath.PathFinderSourceFactions:
                    return "Path data: factions";
                case RuntimeHotpath.PathFinderSourceFog:
                    return "Path data: fog";
                case RuntimeHotpath.PathFinderSourcePersistentDanger:
                    return "Path data: persistent danger";
                case RuntimeHotpath.PathFinderSourceDarkness:
                    return "Path data: darkness";
                case RuntimeHotpath.PathFinderJobBarrier:
                    return "PathFinder job barrier";
                case RuntimeHotpath.PathFinderGridScheduling:
                    return "PathFinder grid scheduling";
                case RuntimeHotpath.PathFinderPathScheduling:
                    return "PathFinder path scheduling";
                case RuntimeHotpath.ReachabilityCanReach:
                    return "Reachability.CanReach";
                case RuntimeHotpath.ReachabilityCacheLookup:
                    return "ReachabilityCache.CachedResultFor";
                default:
                    throw new ArgumentOutOfRangeException(nameof(hotpath));
            }
        }
    }

    internal readonly struct RuntimePathfindingSnapshot
    {
        internal RuntimePathfindingSnapshot(
            long batches,
            long requests,
            long maximumBatchSize,
            long totalQueueDelayTicks,
            long maximumQueueDelayTicks,
            long dataUpdates,
            long dirtyCells,
            long maximumDirtyCells,
            long gridJobsCreated,
            long reachabilityCacheHits,
            long reachabilityCacheMisses)
        {
            Batches = batches;
            Requests = requests;
            MaximumBatchSize = maximumBatchSize;
            TotalQueueDelayTicks = totalQueueDelayTicks;
            MaximumQueueDelayTicks = maximumQueueDelayTicks;
            DataUpdates = dataUpdates;
            DirtyCells = dirtyCells;
            MaximumDirtyCells = maximumDirtyCells;
            GridJobsCreated = gridJobsCreated;
            ReachabilityCacheHits = reachabilityCacheHits;
            ReachabilityCacheMisses = reachabilityCacheMisses;
        }

        internal long Batches { get; }

        internal long Requests { get; }

        internal long MaximumBatchSize { get; }

        internal long TotalQueueDelayTicks { get; }

        internal long MaximumQueueDelayTicks { get; }

        internal long DataUpdates { get; }

        internal long DirtyCells { get; }

        internal long MaximumDirtyCells { get; }

        internal long GridJobsCreated { get; }

        internal long ReachabilityCacheHits { get; }

        internal long ReachabilityCacheMisses { get; }
    }

    internal readonly struct RuntimeProfilingSnapshot
    {
        internal RuntimeProfilingSnapshot(
            ProfileAggregationMode aggregationMode,
            ProfileSnapshot<RuntimeHotpath> hotpaths,
            RuntimePathfindingSnapshot pathfinding)
        {
            AggregationMode = aggregationMode;
            Hotpaths = hotpaths ??
                throw new ArgumentNullException(nameof(hotpaths));
            Pathfinding = pathfinding;
        }

        internal ProfileAggregationMode AggregationMode { get; }

        internal ProfileSnapshot<RuntimeHotpath> Hotpaths { get; }

        internal RuntimePathfindingSnapshot Pathfinding { get; }
    }
}
