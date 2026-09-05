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

    internal readonly struct PathRequestObservation
    {
        internal PathRequestObservation(
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
            TargetKey = targetKey;
            Tick = tick;
            Distance = distance;
            Constraints = constraints;
            PawnCategory = pawnCategory;
            TraversalMode = traversalMode;
            EndMode = endMode;
            TargetKind = targetKind;
            Locality = locality;
        }

        internal long TargetKey { get; }

        internal int Tick { get; }

        internal int Distance { get; }

        internal PathRequestConstraint Constraints { get; }

        internal PathRequestPawnCategory PawnCategory { get; }

        internal PathRequestTraversalMode TraversalMode { get; }

        internal PathRequestEndMode EndMode { get; }

        internal PathRequestTargetKind TargetKind { get; }

        internal PathRequestLocality Locality { get; }
    }

    internal readonly struct PathSpatialObservation
    {
        internal PathSpatialObservation(
            int dirtyCells,
            int expandedCellVisits,
            int uniqueExpandedCells,
            int chunks8,
            int chunks16,
            int chunks32)
        {
            DirtyCells = dirtyCells;
            ExpandedCellVisits = expandedCellVisits;
            UniqueExpandedCells = uniqueExpandedCells;
            Chunks8 = chunks8;
            Chunks16 = chunks16;
            Chunks32 = chunks32;
        }

        internal int DirtyCells { get; }

        internal int ExpandedCellVisits { get; }

        internal int UniqueExpandedCells { get; }

        internal int Chunks8 { get; }

        internal int Chunks16 { get; }

        internal int Chunks32 { get; }
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
        ReachabilityCacheLookup,
        ShadowGridFull,
        ShadowGridIncremental,
        ShadowGridRebuild,
        ShadowGridQuery
    }

    internal static class RuntimeHotpathCatalog
    {
        internal const int Count = 29;

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
                case RuntimeHotpath.PathRequestTelemetry:
                    return "FixWorld path request telemetry";
                case RuntimeHotpath.PathSpatialTelemetry:
                    return "FixWorld path spatial telemetry";
                case RuntimeHotpath.ReachabilityCanReach:
                    return "Reachability.CanReach";
                case RuntimeHotpath.ReachabilityCacheLookup:
                    return "ReachabilityCache.CachedResultFor";
                case RuntimeHotpath.ShadowGridFull:
                    return "ShadowGridObserver full update";
                case RuntimeHotpath.ShadowGridIncremental:
                    return "ShadowGridObserver incremental update";
                case RuntimeHotpath.ShadowGridRebuild:
                    return "ShadowConnectivityGrid.Rebuild";
                case RuntimeHotpath.ShadowGridQuery:
                    return "ShadowConnectivityGrid query";
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
            long reachabilityCacheMisses,
            RuntimePathRequestSnapshot requestDemand,
            RuntimeSpatialSnapshot spatial)
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
            RequestDemand = requestDemand;
            Spatial = spatial;
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

        internal RuntimePathRequestSnapshot RequestDemand { get; }

        internal RuntimeSpatialSnapshot Spatial { get; }
    }

    internal readonly struct RuntimePathRequestSnapshot
    {
        internal RuntimePathRequestSnapshot(
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
            Observations = observations;
            RepeatedTargets = repeatedTargets;
            TargetTrackerCollisions = targetTrackerCollisions;
            TotalDistance = totalDistance;
            MaximumDistance = maximumDistance;
            PawnCategories = pawnCategories ??
                throw new ArgumentNullException(nameof(pawnCategories));
            TraversalModes = traversalModes ??
                throw new ArgumentNullException(nameof(traversalModes));
            EndModes = endModes ??
                throw new ArgumentNullException(nameof(endModes));
            TargetKinds = targetKinds ??
                throw new ArgumentNullException(nameof(targetKinds));
            DistanceBuckets = distanceBuckets ??
                throw new ArgumentNullException(nameof(distanceBuckets));
            Localities = localities ??
                throw new ArgumentNullException(nameof(localities));
            Constraints = constraints ??
                throw new ArgumentNullException(nameof(constraints));
        }

        internal long Observations { get; }

        internal long RepeatedTargets { get; }

        internal long TargetTrackerCollisions { get; }

        internal long TotalDistance { get; }

        internal long MaximumDistance { get; }

        internal long[] PawnCategories { get; }

        internal long[] TraversalModes { get; }

        internal long[] EndModes { get; }

        internal long[] TargetKinds { get; }

        internal long[] DistanceBuckets { get; }

        internal long[] Localities { get; }

        internal long[] Constraints { get; }
    }

    internal readonly struct RuntimeSpatialSnapshot
    {
        internal RuntimeSpatialSnapshot(
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
            ExpandedCellVisits = expandedCellVisits;
            UniqueExpandedCells = uniqueExpandedCells;
            Chunks8 = chunks8;
            Chunks16 = chunks16;
            Chunks32 = chunks32;
            MaximumUniqueExpandedCells = maximumUniqueExpandedCells;
            MaximumChunks8 = maximumChunks8;
            MaximumChunks16 = maximumChunks16;
            MaximumChunks32 = maximumChunks32;
        }

        internal long ExpandedCellVisits { get; }

        internal long UniqueExpandedCells { get; }

        internal long Chunks8 { get; }

        internal long Chunks16 { get; }

        internal long Chunks32 { get; }

        internal long MaximumUniqueExpandedCells { get; }

        internal long MaximumChunks8 { get; }

        internal long MaximumChunks16 { get; }

        internal long MaximumChunks32 { get; }
    }

    internal readonly struct RuntimeShadowGridSnapshot
    {
        internal RuntimeShadowGridSnapshot(
            long fullUpdates,
            long incrementalUpdates,
            long sampledCells,
            long changedCells,
            long rebuiltLeaves,
            long changedLeaves,
            long rebuiltRegions,
            long changedRegions,
            long rebuiltSuperChunks,
            long changedSuperChunks,
            long failures,
            long queriesAnswered,
            long queriesConnected,
            long queriesUnavailable,
            long queriesEligible,
            long queriesMismatched,
            long queriesEligibleNegative,
            long queriesNegativeMatches,
            long[] queryUnavailableReasons)
        {
            FullUpdates = fullUpdates;
            IncrementalUpdates = incrementalUpdates;
            SampledCells = sampledCells;
            ChangedCells = changedCells;
            RebuiltLeaves = rebuiltLeaves;
            ChangedLeaves = changedLeaves;
            RebuiltRegions = rebuiltRegions;
            ChangedRegions = changedRegions;
            RebuiltSuperChunks = rebuiltSuperChunks;
            ChangedSuperChunks = changedSuperChunks;
            Failures = failures;
            QueriesAnswered = queriesAnswered;
            QueriesConnected = queriesConnected;
            QueriesUnavailable = queriesUnavailable;
            QueriesEligible = queriesEligible;
            QueriesMismatched = queriesMismatched;
            QueriesEligibleNegative = queriesEligibleNegative;
            QueriesNegativeMatches = queriesNegativeMatches;
            QueryUnavailableReasons = queryUnavailableReasons ??
                throw new ArgumentNullException(nameof(queryUnavailableReasons));
        }

        internal long FullUpdates { get; }

        internal long IncrementalUpdates { get; }

        internal long SampledCells { get; }

        internal long ChangedCells { get; }

        internal long RebuiltLeaves { get; }

        internal long ChangedLeaves { get; }

        internal long RebuiltRegions { get; }

        internal long ChangedRegions { get; }

        internal long RebuiltSuperChunks { get; }

        internal long ChangedSuperChunks { get; }

        internal long Failures { get; }

        internal long QueriesAnswered { get; }

        internal long QueriesConnected { get; }

        internal long QueriesUnavailable { get; }

        internal long QueriesEligible { get; }

        internal long QueriesMismatched { get; }

        internal long QueriesEligibleNegative { get; }

        internal long QueriesNegativeMatches { get; }

        internal long[] QueryUnavailableReasons { get; }
    }

    internal readonly struct RuntimeProfilingSnapshot
    {
        internal RuntimeProfilingSnapshot(
            ProfileAggregationMode aggregationMode,
            ProfileSnapshot<RuntimeHotpath> hotpaths,
            RuntimePathfindingSnapshot pathfinding,
            RuntimeShadowGridSnapshot shadowGrid = default)
        {
            AggregationMode = aggregationMode;
            Hotpaths = hotpaths ??
                throw new ArgumentNullException(nameof(hotpaths));
            Pathfinding = pathfinding;
            ShadowGrid = shadowGrid;
        }

        internal ProfileAggregationMode AggregationMode { get; }

        internal ProfileSnapshot<RuntimeHotpath> Hotpaths { get; }

        internal RuntimePathfindingSnapshot Pathfinding { get; }

        internal RuntimeShadowGridSnapshot ShadowGrid { get; }
    }
}
