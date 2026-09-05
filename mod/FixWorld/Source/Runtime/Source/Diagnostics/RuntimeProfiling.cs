using System;
using FixWorld.Profiling;

namespace FixWorld.Diagnostics
{
    internal enum PathRequestPawnCategory : byte
    {
        Colonist,
        Animal,
        Wildlife,
        Hostile,
        Mechanoid,
        Other
    }

    internal enum PathRequestTraversalMode : byte
    {
        ByPawn,
        PassDoors,
        NoPassClosedDoors,
        PassAllDestroyableThings,
        PassAllDestroyablePlayerOwnedThings,
        NoPassClosedDoorsOrWater,
        PassAllDestroyableThingsNotWater,
        Unknown
    }

    internal enum PathRequestEndMode : byte
    {
        None,
        OnCell,
        Touch,
        ClosestTouch,
        InteractionCell,
        Unknown
    }

    internal enum PathRequestTargetKind : byte
    {
        Cell,
        Thing,
        Pawn,
        Invalid
    }

    internal enum PathRequestDistanceBucket : byte
    {
        UpTo16,
        UpTo32,
        UpTo64,
        UpTo128,
        Over128
    }

    internal enum PathRequestLocality : byte
    {
        SameLeaf,
        SameRegion,
        SameSuperChunk,
        CrossSuperChunk,
        Invalid
    }

    [Flags]
    internal enum PathRequestConstraint : ushort
    {
        None = 0,
        AllowedArea = 1 << 0,
        Customizer = 1 << 1,
        BashDoors = 1 << 2,
        BashFences = 1 << 3,
        AvoidGrid = 1 << 4,
        FenceBlocked = 1 << 5,
        PersistentDanger = 1 << 6,
        Darkness = 1 << 7,
        Fog = 1 << 8
    }

    internal static class PathRequestCatalog
    {
        internal const int PawnCategoryCount = 6;
        internal const int TraversalModeCount = 8;
        internal const int EndModeCount = 6;
        internal const int TargetKindCount = 4;
        internal const int DistanceBucketCount = 5;
        internal const int LocalityCount = 5;
        internal const int ConstraintCount = 9;

        internal static string GetName(PathRequestPawnCategory category) =>
            category switch
            {
                PathRequestPawnCategory.Colonist => "colonists",
                PathRequestPawnCategory.Animal => "animals",
                PathRequestPawnCategory.Wildlife => "wildlife",
                PathRequestPawnCategory.Hostile => "hostiles",
                PathRequestPawnCategory.Mechanoid => "mechanoids",
                PathRequestPawnCategory.Other => "other",
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };

        internal static string GetName(PathRequestTraversalMode mode) =>
            mode switch
            {
                PathRequestTraversalMode.ByPawn => "by pawn",
                PathRequestTraversalMode.PassDoors => "pass doors",
                PathRequestTraversalMode.NoPassClosedDoors =>
                    "closed doors blocked",
                PathRequestTraversalMode.PassAllDestroyableThings =>
                    "destroyable things",
                PathRequestTraversalMode.PassAllDestroyablePlayerOwnedThings =>
                    "player destroyables",
                PathRequestTraversalMode.NoPassClosedDoorsOrWater =>
                    "doors and water blocked",
                PathRequestTraversalMode.PassAllDestroyableThingsNotWater =>
                    "destroyables, no water",
                PathRequestTraversalMode.Unknown => "unknown",
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };

        internal static string GetName(PathRequestEndMode mode) =>
            mode switch
            {
                PathRequestEndMode.None => "none",
                PathRequestEndMode.OnCell => "on cell",
                PathRequestEndMode.Touch => "touch",
                PathRequestEndMode.ClosestTouch => "closest touch",
                PathRequestEndMode.InteractionCell => "interaction cell",
                PathRequestEndMode.Unknown => "unknown",
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };

        internal static string GetName(PathRequestTargetKind kind) =>
            kind switch
            {
                PathRequestTargetKind.Cell => "cells",
                PathRequestTargetKind.Thing => "things",
                PathRequestTargetKind.Pawn => "pawns",
                PathRequestTargetKind.Invalid => "invalid",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

        internal static string GetName(PathRequestDistanceBucket bucket) =>
            bucket switch
            {
                PathRequestDistanceBucket.UpTo16 => "0-16",
                PathRequestDistanceBucket.UpTo32 => "17-32",
                PathRequestDistanceBucket.UpTo64 => "33-64",
                PathRequestDistanceBucket.UpTo128 => "65-128",
                PathRequestDistanceBucket.Over128 => "129+",
                _ => throw new ArgumentOutOfRangeException(nameof(bucket))
            };

        internal static string GetName(PathRequestLocality locality) =>
            locality switch
            {
                PathRequestLocality.SameLeaf => "same 8x8 leaf",
                PathRequestLocality.SameRegion => "same 16x16 region only",
                PathRequestLocality.SameSuperChunk =>
                    "same 32x32 super-chunk only",
                PathRequestLocality.CrossSuperChunk => "cross super-chunk",
                PathRequestLocality.Invalid => "invalid",
                _ => throw new ArgumentOutOfRangeException(nameof(locality))
            };

        internal static string GetName(PathRequestConstraint constraint) =>
            constraint switch
            {
                PathRequestConstraint.AllowedArea => "area",
                PathRequestConstraint.Customizer => "customizer",
                PathRequestConstraint.BashDoors => "bash doors",
                PathRequestConstraint.BashFences => "bash fences",
                PathRequestConstraint.AvoidGrid => "avoid grid",
                PathRequestConstraint.FenceBlocked => "fence blocked",
                PathRequestConstraint.PersistentDanger => "persistent danger",
                PathRequestConstraint.Darkness => "darkness",
                PathRequestConstraint.Fog => "fog",
                _ => throw new ArgumentOutOfRangeException(nameof(constraint))
            };
    }

    internal readonly struct PathRequestObservation(
        long targetKey,
        int tick,
        int distance,
        PathRequestConstraint constraints,
        PathRequestPawnCategory pawnCategory,
        PathRequestTraversalMode traversalMode,
        PathRequestEndMode endMode,
        PathRequestTargetKind targetKind,
        PathRequestLocality locality)
    {
        internal long TargetKey { get; } = targetKey;

        internal int Tick { get; } = tick;

        internal int Distance { get; } = distance;

        internal PathRequestConstraint Constraints { get; } = constraints;

        internal PathRequestPawnCategory PawnCategory { get; } = pawnCategory;

        internal PathRequestTraversalMode TraversalMode { get; } = traversalMode;

        internal PathRequestEndMode EndMode { get; } = endMode;

        internal PathRequestTargetKind TargetKind { get; } = targetKind;

        internal PathRequestLocality Locality { get; } = locality;
    }

    internal readonly struct PathSpatialObservation(
        int dirtyCells,
        int expandedCellVisits,
        int uniqueExpandedCells,
        int chunks8,
        int chunks16,
        int chunks32)
    {
        internal int DirtyCells { get; } = dirtyCells;

        internal int ExpandedCellVisits { get; } = expandedCellVisits;

        internal int UniqueExpandedCells { get; } = uniqueExpandedCells;

        internal int Chunks8 { get; } = chunks8;

        internal int Chunks16 { get; } = chunks16;

        internal int Chunks32 { get; } = chunks32;
    }

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
        PathRequestTelemetry,
        PathSpatialTelemetry,
        ReachabilityCanReach,
        ReachabilityCacheLookup
    }

    internal static class RuntimeHotpathCatalog
    {
        internal const int Count = 25;

        internal static string GetName(RuntimeHotpath hotpath)
        {
            return hotpath switch
            {
                RuntimeHotpath.Tick => "TickManager.DoSingleTick",
                RuntimeHotpath.MapPreTick => "Map.MapPreTick",
                RuntimeHotpath.MapPostTick => "Map.MapPostTick",
                RuntimeHotpath.PathFinderTick => "PathFinder.PathFinderTick",
                RuntimeHotpath.PathFinderPushRequest => "PathFinder.PushRequest",
                RuntimeHotpath.PathFinderFindPathNow => "PathFinder.FindPathNow",
                RuntimeHotpath.PathFinderGatherMapData => "PathFinderMapData.GatherData",
                RuntimeHotpath.PathFinderSourceCosts => "Path data: movement costs",
                RuntimeHotpath.PathFinderSourceAreas => "Path data: areas",
                RuntimeHotpath.PathFinderSourcePerceptual => "Path data: perceptual costs",
                RuntimeHotpath.PathFinderSourceConnectivity => "Path data: connectivity",
                RuntimeHotpath.PathFinderSourceWater => "Path data: water",
                RuntimeHotpath.PathFinderSourceFences => "Path data: fences",
                RuntimeHotpath.PathFinderSourceBuildings => "Path data: buildings",
                RuntimeHotpath.PathFinderSourceFactions => "Path data: factions",
                RuntimeHotpath.PathFinderSourceFog => "Path data: fog",
                RuntimeHotpath.PathFinderSourcePersistentDanger => "Path data: persistent danger",
                RuntimeHotpath.PathFinderSourceDarkness => "Path data: darkness",
                RuntimeHotpath.PathFinderJobBarrier => "PathFinder job barrier",
                RuntimeHotpath.PathFinderGridScheduling => "PathFinder grid scheduling",
                RuntimeHotpath.PathFinderPathScheduling => "PathFinder path scheduling",
                RuntimeHotpath.PathRequestTelemetry => "FixWorld path request telemetry",
                RuntimeHotpath.PathSpatialTelemetry => "FixWorld path spatial telemetry",
                RuntimeHotpath.ReachabilityCanReach => "Reachability.CanReach",
                RuntimeHotpath.ReachabilityCacheLookup => "ReachabilityCache.CachedResultFor",
                _ => throw new ArgumentOutOfRangeException(nameof(hotpath)),
            };
        }
    }

    internal readonly struct RuntimePathfindingSnapshot(
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
        long reachabilityCacheMisses,
        RuntimePathRequestSnapshot requestDemand,
        RuntimeSpatialSnapshot spatial)
    {
        internal long Batches { get; } = batches;

        internal long Requests { get; } = requests;

        internal long MaximumBatchSize { get; } = maximumBatchSize;

        internal long TotalQueueDelayTicks { get; } = totalQueueDelayTicks;

        internal long MaximumQueueDelayTicks { get; } = maximumQueueDelayTicks;

        internal long DataUpdates { get; } = dataUpdates;

        internal long DirtyCells { get; } = dirtyCells;

        internal long MaximumDirtyCells { get; } = maximumDirtyCells;

        internal long GridJobsCreated { get; } = gridJobsCreated;

        internal long ReachabilityCacheHits { get; } = reachabilityCacheHits;

        internal long ReachabilityCacheMisses { get; } = reachabilityCacheMisses;

        internal RuntimePathRequestSnapshot RequestDemand { get; } = requestDemand;

        internal RuntimeSpatialSnapshot Spatial { get; } = spatial;
    }

    internal readonly struct RuntimePathRequestSnapshot(
        long observations,
        long repeatedTargets,
        long targetTrackerCollisions,
        long totalDistance,
        long maximumDistance,
        long[] pawnCategories,
        long[] traversalModes,
        long[] endModes,
        long[] targetKinds,
        long[] distanceBuckets,
        long[] localities,
        long[] constraints)
    {
        internal long Observations { get; } = observations;

        internal long RepeatedTargets { get; } = repeatedTargets;

        internal long TargetTrackerCollisions { get; } = targetTrackerCollisions;

        internal long TotalDistance { get; } = totalDistance;

        internal long MaximumDistance { get; } = maximumDistance;

        internal long[] PawnCategories { get; } = pawnCategories ??
                throw new ArgumentNullException(nameof(pawnCategories));

        internal long[] TraversalModes { get; } = traversalModes ??
                throw new ArgumentNullException(nameof(traversalModes));

        internal long[] EndModes { get; } = endModes ??
                throw new ArgumentNullException(nameof(endModes));

        internal long[] TargetKinds { get; } = targetKinds ??
                throw new ArgumentNullException(nameof(targetKinds));

        internal long[] DistanceBuckets { get; } = distanceBuckets ??
                throw new ArgumentNullException(nameof(distanceBuckets));

        internal long[] Localities { get; } = localities ??
                throw new ArgumentNullException(nameof(localities));

        internal long[] Constraints { get; } = constraints ??
                throw new ArgumentNullException(nameof(constraints));
    }

    internal readonly struct RuntimeSpatialSnapshot(
        long expandedCellVisits,
        long uniqueExpandedCells,
        long chunks8,
        long chunks16,
        long chunks32,
        long maximumUniqueExpandedCells,
        long maximumChunks8,
        long maximumChunks16,
        long maximumChunks32)
    {
        internal long ExpandedCellVisits { get; } = expandedCellVisits;

        internal long UniqueExpandedCells { get; } = uniqueExpandedCells;

        internal long Chunks8 { get; } = chunks8;

        internal long Chunks16 { get; } = chunks16;

        internal long Chunks32 { get; } = chunks32;

        internal long MaximumUniqueExpandedCells { get; } = maximumUniqueExpandedCells;

        internal long MaximumChunks8 { get; } = maximumChunks8;

        internal long MaximumChunks16 { get; } = maximumChunks16;

        internal long MaximumChunks32 { get; } = maximumChunks32;
    }

    internal readonly struct RuntimeProfilingSnapshot(
        ProfileAggregationMode aggregationMode,
        ProfileSnapshot<RuntimeHotpath> hotpaths,
        RuntimePathfindingSnapshot pathfinding,
        double ticksPerSecond = 0,
        bool paused = true)
    {
        internal ProfileAggregationMode AggregationMode { get; } = aggregationMode;

        internal ProfileSnapshot<RuntimeHotpath> Hotpaths { get; } = hotpaths ??
                throw new ArgumentNullException(nameof(hotpaths));

        internal RuntimePathfindingSnapshot Pathfinding { get; } = pathfinding;

        internal double TicksPerSecond { get; } = ticksPerSecond;

        internal bool Paused { get; } = paused;
    }
}
