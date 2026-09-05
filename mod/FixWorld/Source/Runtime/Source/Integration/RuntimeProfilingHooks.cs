using System;
using System.Collections.Generic;
using System.Reflection;
using FixWorld.Diagnostics;
using FixWorld.Pathfinding;
using FixWorld.Runtime;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace FixWorld.Integration
{
    internal static class RuntimeProfilingHooks
    {
        [ThreadStatic]
        private static PathSpatialScratch pathSpatialScratch;

        internal static readonly Type[] PatchTypes =
        [
            typeof(TickPatch),
            typeof(MapPreTickPatch),
            typeof(MapPostTickPatch),
            typeof(PathFinderTickPatch),
            typeof(PathFinderCreateRequestPatch),
            typeof(PathFinderPushRequestPatch),
            typeof(PathFinderFindPathNowPatch),
            typeof(PathFinderGatherMapDataPatch),
            typeof(PathFinderDataSourcePatch),
            typeof(ConnectivityFullBuildPatch),
            typeof(PathFinderJobBarrierPatch),
            typeof(PathFinderGridSchedulingPatch),
            typeof(PathFinderPathSchedulingPatch),
            typeof(ReachabilityCanReachPatch),
            typeof(ReachabilityCacheLookupPatch)
        ];

        private static long Begin(RuntimeHotpath hotpath) =>
            RuntimeHost.StartRuntimeHotpath(hotpath);

        private static void End(RuntimeHotpath hotpath, long startedAt) =>
            RuntimeHost.StopRuntimeHotpath(hotpath, startedAt);

        private readonly struct HotpathState
        {
            internal HotpathState(RuntimeHotpath hotpath)
            {
                Hotpath = hotpath;
                StartedAt = Begin(hotpath);
            }

            internal RuntimeHotpath Hotpath { get; }

            internal long StartedAt { get; }
        }

        private static RuntimeHotpath GetDataSourceHotpath(object source)
        {
            if (source is CostSource)
            {
                return RuntimeHotpath.PathFinderSourceCosts;
            }

            if (source is AreaSource)
            {
                return RuntimeHotpath.PathFinderSourceAreas;
            }

            if (source is PerceptualSource)
            {
                return RuntimeHotpath.PathFinderSourcePerceptual;
            }

            if (source is ConnectivitySource)
            {
                return RuntimeHotpath.PathFinderSourceConnectivity;
            }

            if (source is WaterSource)
            {
                return RuntimeHotpath.PathFinderSourceWater;
            }

            if (source is FenceSource)
            {
                return RuntimeHotpath.PathFinderSourceFences;
            }

            if (source is BuildingSource)
            {
                return RuntimeHotpath.PathFinderSourceBuildings;
            }

            if (source is FactionSource)
            {
                return RuntimeHotpath.PathFinderSourceFactions;
            }

            if (source is FogSource)
            {
                return RuntimeHotpath.PathFinderSourceFog;
            }

            if (source is PersistentDangerSource)
            {
                return RuntimeHotpath.PathFinderSourcePersistentDanger;
            }

            if (source is DarknessSource)
            {
                return RuntimeHotpath.PathFinderSourceDarkness;
            }

            throw new ArgumentOutOfRangeException(nameof(source));
        }

        private static PathRequestObservation Observe(PathRequest request)
        {
            TraverseParms traversal = request.TraverseParms;
            LocalTargetInfo target = request.Target;
            Pawn pawn = request.pawn ?? traversal.pawn;
            PathRequestConstraint constraints = PathRequestConstraint.None;
            if (request.area != null)
            {
                constraints |= PathRequestConstraint.AllowedArea;
            }

            if (request.customizer != null)
            {
                constraints |= PathRequestConstraint.Customizer;
            }

            if (traversal.canBashDoors)
            {
                constraints |= PathRequestConstraint.BashDoors;
            }

            if (traversal.canBashFences)
            {
                constraints |= PathRequestConstraint.BashFences;
            }

            if (traversal.alwaysUseAvoidGrid)
            {
                constraints |= PathRequestConstraint.AvoidGrid;
            }

            if (traversal.fenceBlocked)
            {
                constraints |= PathRequestConstraint.FenceBlocked;
            }

            if (traversal.avoidPersistentDanger)
            {
                constraints |= PathRequestConstraint.PersistentDanger;
            }

            if (traversal.avoidDarknessDanger)
            {
                constraints |= PathRequestConstraint.Darkness;
            }

            if (traversal.avoidFog)
            {
                constraints |= PathRequestConstraint.Fog;
            }

            int distance = target.IsValid
                ? Math.Abs(request.Start.x - target.Cell.x) +
                  Math.Abs(request.Start.z - target.Cell.z)
                : 0;
            return new PathRequestObservation(
                GetTargetKey(request, target),
                GenTicks.TicksGame,
                distance,
                constraints,
                Classify(pawn),
                Classify(traversal.mode),
                Classify(request.EndMode),
                Classify(target),
                Classify(request.Start, target));
        }

        private static PathRequestPawnCategory Classify(Pawn pawn)
        {
            if (pawn == null)
            {
                return PathRequestPawnCategory.Other;
            }

            if (pawn.RaceProps.IsMechanoid)
            {
                return PathRequestPawnCategory.Mechanoid;
            }

            if (pawn.IsColonist)
            {
                return PathRequestPawnCategory.Colonist;
            }

            if (Faction.OfPlayer != null && pawn.HostileTo(Faction.OfPlayer))
            {
                return PathRequestPawnCategory.Hostile;
            }

            if (!pawn.RaceProps.Animal)
            {
                return PathRequestPawnCategory.Other;
            }

            return pawn.Faction == null
                ? PathRequestPawnCategory.Wildlife
                : PathRequestPawnCategory.Animal;
        }

        private static PathRequestTraversalMode Classify(TraverseMode mode) =>
            mode switch
            {
                TraverseMode.ByPawn => PathRequestTraversalMode.ByPawn,
                TraverseMode.PassDoors => PathRequestTraversalMode.PassDoors,
                TraverseMode.NoPassClosedDoors =>
                    PathRequestTraversalMode.NoPassClosedDoors,
                TraverseMode.PassAllDestroyableThings =>
                    PathRequestTraversalMode.PassAllDestroyableThings,
                TraverseMode.PassAllDestroyablePlayerOwnedThings =>
                    PathRequestTraversalMode
                        .PassAllDestroyablePlayerOwnedThings,
                TraverseMode.NoPassClosedDoorsOrWater =>
                    PathRequestTraversalMode.NoPassClosedDoorsOrWater,
                TraverseMode.PassAllDestroyableThingsNotWater =>
                    PathRequestTraversalMode
                        .PassAllDestroyableThingsNotWater,
                _ => PathRequestTraversalMode.Unknown
            };

        private static PathRequestEndMode Classify(PathEndMode mode) =>
            mode switch
            {
                PathEndMode.None => PathRequestEndMode.None,
                PathEndMode.OnCell => PathRequestEndMode.OnCell,
                PathEndMode.Touch => PathRequestEndMode.Touch,
                PathEndMode.ClosestTouch => PathRequestEndMode.ClosestTouch,
                PathEndMode.InteractionCell =>
                    PathRequestEndMode.InteractionCell,
                _ => PathRequestEndMode.Unknown
            };

        private static PathRequestTargetKind Classify(
            LocalTargetInfo target)
        {
            if (!target.IsValid)
            {
                return PathRequestTargetKind.Invalid;
            }

            if (target.Pawn != null)
            {
                return PathRequestTargetKind.Pawn;
            }

            return target.HasThing
                ? PathRequestTargetKind.Thing
                : PathRequestTargetKind.Cell;
        }

        private static PathRequestLocality Classify(
            IntVec3 start,
            LocalTargetInfo target)
        {
            if (!start.IsValid || !target.IsValid)
            {
                return PathRequestLocality.Invalid;
            }

            IntVec3 destination = target.Cell;
            if (!destination.IsValid)
            {
                return PathRequestLocality.Invalid;
            }

            if ((start.x >> 3) == (destination.x >> 3) &&
                (start.z >> 3) == (destination.z >> 3))
            {
                return PathRequestLocality.SameLeaf;
            }

            if ((start.x >> 4) == (destination.x >> 4) &&
                (start.z >> 4) == (destination.z >> 4))
            {
                return PathRequestLocality.SameRegion;
            }

            if ((start.x >> 5) == (destination.x >> 5) &&
                (start.z >> 5) == (destination.z >> 5))
            {
                return PathRequestLocality.SameSuperChunk;
            }

            return PathRequestLocality.CrossSuperChunk;
        }

        private static long GetTargetKey(
            PathRequest request,
            LocalTargetInfo target)
        {
            unchecked
            {
                const ulong offset = 1469598103934665603UL;
                const ulong prime = 1099511628211UL;
                ulong hash = (offset ^ (uint)(request.map?.uniqueID ?? -1)) *
                             prime;
                if (target.HasThing)
                {
                    hash = (hash ^ 1UL) * prime;
                    hash = (hash ^ (uint)target.Thing.thingIDNumber) * prime;
                }
                else
                {
                    hash = (hash ^ 2UL) * prime;
                    hash = (hash ^ (uint)target.Cell.x) * prime;
                    hash = (hash ^ (uint)target.Cell.z) * prime;
                }

                hash = (hash ^ (byte)request.EndMode) * prime;
                long key = (long)hash;
                return key == 0L ? 1L : key;
            }
        }

        private static PathSpatialObservation ObserveSpatialChanges(
            Map map,
            List<IntVec3> dirtyCells)
        {
            PathSpatialScratch scratch = pathSpatialScratch ??=
                new PathSpatialScratch();
            scratch.Clear();
            int expandedCellVisits = 0;
            int sizeX = map.Size.x;
            int sizeZ = map.Size.z;
            for (int index = 0; index < dirtyCells.Count; index++)
            {
                IntVec3 dirty = dirtyCells[index];
                int minimumX = Math.Max(0, dirty.x - 1);
                int maximumX = Math.Min(sizeX - 1, dirty.x + 1);
                int minimumZ = Math.Max(0, dirty.z - 1);
                int maximumZ = Math.Min(sizeZ - 1, dirty.z + 1);
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    for (int x = minimumX; x <= maximumX; x++)
                    {
                        expandedCellVisits++;
                        scratch.ExpandedCells.Add(x + (z * sizeX));
                        scratch.Chunks8.Add((x >> 3) | ((z >> 3) << 16));
                        scratch.Chunks16.Add((x >> 4) | ((z >> 4) << 16));
                        scratch.Chunks32.Add((x >> 5) | ((z >> 5) << 16));
                    }
                }
            }

            return new PathSpatialObservation(
                dirtyCells.Count,
                expandedCellVisits,
                scratch.ExpandedCells.Count,
                scratch.Chunks8.Count,
                scratch.Chunks16.Count,
                scratch.Chunks32.Count);
        }

        [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
        private static class TickPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.Tick);

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.Tick, __state);
        }

        [HarmonyPatch(typeof(Map), nameof(Map.MapPreTick))]
        private static class MapPreTickPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.MapPreTick);

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.MapPreTick, __state);
        }

        [HarmonyPatch(typeof(Map), nameof(Map.MapPostTick))]
        private static class MapPostTickPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.MapPostTick);

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.MapPostTick, __state);
        }

        [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.PathFinderTick))]
        private static class PathFinderTickPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.PathFinderTick);

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.PathFinderTick, __state);
        }

        [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.PushRequest))]
        private static class PathFinderPushRequestPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.PathFinderPushRequest);

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.PathFinderPushRequest, __state);
        }

        [HarmonyPatch]
        private static class PathFinderCreateRequestPatch
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(
                    typeof(PathFinder),
                    nameof(PathFinder.CreateRequest),
                    [
                        typeof(IntVec3),
                        typeof(LocalTargetInfo),
                        typeof(Nullable<IntVec3>),
                        typeof(TraverseParms),
                        typeof(Nullable<PathFinderCostTuning>),
                        typeof(PathEndMode),
                        typeof(Pawn),
                        typeof(PathRequest.IPathGridCustomizer)
                    ]) ??
                throw new MissingMethodException(
                    typeof(PathFinder).FullName,
                    nameof(PathFinder.CreateRequest));

            [HarmonyPostfix]
            private static void Postfix(PathRequest __result)
            {
                if (__result == null)
                {
                    return;
                }

                long startedAt = Begin(RuntimeHotpath.PathRequestTelemetry);
                PathRequestObservation observation = Observe(__result);
                RuntimeHost.ObservePathRequest(in observation);
                End(RuntimeHotpath.PathRequestTelemetry, startedAt);
            }
        }

        [HarmonyPatch]
        private static class PathFinderFindPathNowPatch
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(
                    typeof(PathFinder),
                    nameof(PathFinder.FindPathNow),
                    [
                        typeof(IntVec3),
                        typeof(LocalTargetInfo),
                        typeof(TraverseParms),
                        typeof(Nullable<PathFinderCostTuning>),
                        typeof(PathEndMode),
                        typeof(PathRequest.IPathGridCustomizer)
                    ]) ??
                throw new MissingMethodException(
                    typeof(PathFinder).FullName,
                    nameof(PathFinder.FindPathNow));

            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.PathFinderFindPathNow);

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.PathFinderFindPathNow, __state);
        }

        [HarmonyPatch(
            typeof(PathFinderMapData),
            nameof(PathFinderMapData.GatherData))]
        private static class PathFinderGatherMapDataPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.PathFinderGatherMapData);

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.PathFinderGatherMapData, __state);
        }

        [HarmonyPatch]
        private static class PathFinderDataSourcePatch
        {
            private static readonly Type[] SourceTypes =
            [
                typeof(CostSource),
                typeof(AreaSource),
                typeof(PerceptualSource),
                typeof(ConnectivitySource),
                typeof(WaterSource),
                typeof(FenceSource),
                typeof(BuildingSource),
                typeof(FactionSource),
                typeof(FogSource),
                typeof(PersistentDangerSource),
                typeof(DarknessSource)
            ];

            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (Type sourceType in SourceTypes)
                {
                    yield return AccessTools.DeclaredMethod(
                        sourceType,
                        nameof(IPathFinderDataSource.UpdateIncrementally)) ??
                        throw new MissingMethodException(
                            sourceType.FullName,
                            nameof(IPathFinderDataSource.UpdateIncrementally));
                }
            }

            [HarmonyPrefix]
            private static void Prefix(
                object __instance,
                List<IntVec3> __1,
                Map ___map,
                out HotpathState __state)
            {
                if (__instance is ConnectivitySource)
                {
                    long startedAt = Begin(RuntimeHotpath.PathSpatialTelemetry);
                    PathSpatialObservation observation =
                        ObserveSpatialChanges(___map, __1);
                    RuntimeHost.ObservePathDataUpdate(in observation);
                    End(RuntimeHotpath.PathSpatialTelemetry, startedAt);
                }

                __state = new HotpathState(
                    GetDataSourceHotpath(__instance));
            }

            [HarmonyPostfix]
            private static void Postfix(
                object __instance,
                Map ___map,
                List<IntVec3> __1,
                HotpathState __state)
            {
                End(__state.Hotpath, __state.StartedAt);
                if (__instance is ConnectivitySource && ShadowGridObserver.Enabled)
                {
                    RuntimeHost.ObserveShadowGrid(___map, __1, fullRebuild: false);
                }
            }
        }

        [HarmonyPatch(typeof(ConnectivitySource), nameof(ConnectivitySource.ComputeAll))]
        private static class ConnectivityFullBuildPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Map ___map)
            {
                if (ShadowGridObserver.Enabled)
                {
                    RuntimeHost.ObserveShadowGrid(___map, null, fullRebuild: true);
                }
            }
        }

        [HarmonyPatch]
        private static class PathFinderJobBarrierPatch
        {
            private static MethodBase TargetMethod() =>
                RequirePathFinderMethod("ForceCompleteScheduledJobs");

            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.PathFinderJobBarrier);

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.PathFinderJobBarrier, __state);
        }

        [HarmonyPatch]
        private static class PathFinderGridSchedulingPatch
        {
            private static MethodBase TargetMethod() =>
                RequirePathFinderMethod("ScheduleGridJob");

            [HarmonyPrefix]
            private static void Prefix(out long __state)
            {
                RuntimeHost.ObservePathGridJobCreated();
                __state = Begin(RuntimeHotpath.PathFinderGridScheduling);
            }

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.PathFinderGridScheduling, __state);
        }

        [HarmonyPatch]
        private static class PathFinderPathSchedulingPatch
        {
            private static MethodBase TargetMethod() =>
                RequirePathFinderMethod("ScheduleBatchedPathJobs");

            [HarmonyPrefix]
            private static void Prefix(
                List<PathRequest> ___tmpCurrentWork,
                out long __state)
            {
                int currentTick = GenTicks.TicksGame;
                long totalQueueDelay = 0L;
                int maximumQueueDelay = 0;
                for (int index = 0; index < ___tmpCurrentWork.Count; index++)
                {
                    int delay = Math.Max(
                        0,
                        currentTick - ___tmpCurrentWork[index].TickStart);
                    totalQueueDelay += delay;
                    maximumQueueDelay = Math.Max(maximumQueueDelay, delay);
                }

                RuntimeHost.ObservePathBatch(
                    ___tmpCurrentWork.Count,
                    totalQueueDelay,
                    maximumQueueDelay);
                ProbeShadowGridQuery(___tmpCurrentWork);
                __state = Begin(RuntimeHotpath.PathFinderPathScheduling);
            }

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.PathFinderPathScheduling, __state);
        }

        private static void ProbeShadowGridQuery(List<PathRequest> requests)
        {
            if (!ShadowGridObserver.Enabled || requests == null)
            {
                return;
            }

            long startedAt = long.MinValue;
            bool queryAttempted = false;
            try
            {
                startedAt = Begin(RuntimeHotpath.ShadowGridQuery);
                int sampleCount = Math.Min(requests.Count, 8);
                for (int index = 0; index < sampleCount; index++)
                {
                    PathRequest request = requests[index];
                    if (request == null)
                    {
                        continue;
                    }

                    PathEndMode endMode = request.EndMode;
                    Map map = request.map;
                    IntVec3 start = request.Start;
                    LocalTargetInfo target = request.Target;
                    if (endMode != PathEndMode.OnCell || map == null ||
                        !start.IsValid || !target.IsValid ||
                        !target.Cell.IsValid)
                    {
                        continue;
                    }

                    IntVec3 destination = target.Cell;
                    queryAttempted = true;
                    bool answered = RuntimeHost.TryQueryShadowGrid(
                        map,
                        start,
                        destination,
                        out bool connected,
                        out _);
                    RuntimeHost.ObserveShadowGridQuery(answered, connected);
                    return;
                }
            }
            catch
            {
                if (queryAttempted)
                {
                    // Shadow probes are observational. A query adapter failure
                    // must never abort the game's path batch.
                    try
                    {
                        RuntimeHost.ObserveShadowGridQuery(false, false);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                if (startedAt != long.MinValue)
                {
                    try
                    {
                        End(RuntimeHotpath.ShadowGridQuery, startedAt);
                    }
                    catch
                    {
                    }
                }
            }
        }

        [HarmonyPatch]
        private static class ReachabilityCanReachPatch
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(
                    typeof(Reachability),
                    nameof(Reachability.CanReach),
                    [
                        typeof(IntVec3),
                        typeof(LocalTargetInfo),
                        typeof(PathEndMode),
                        typeof(TraverseParms)
                    ]) ??
                throw new MissingMethodException(
                    typeof(Reachability).FullName,
                    nameof(Reachability.CanReach));

            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.ReachabilityCanReach);

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.ReachabilityCanReach, __state);
        }

        [HarmonyPatch(
            typeof(ReachabilityCache),
            nameof(ReachabilityCache.CachedResultFor))]
        private static class ReachabilityCacheLookupPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out long __state) =>
                __state = Begin(RuntimeHotpath.ReachabilityCacheLookup);

            [HarmonyPostfix]
            private static void Postfix(
                BoolUnknown __result,
                long __state)
            {
                End(RuntimeHotpath.ReachabilityCacheLookup, __state);
                RuntimeHost.ObserveReachabilityCache(
                    __result != BoolUnknown.Unknown);
            }
        }

        private static MethodBase RequirePathFinderMethod(string name) =>
            AccessTools.Method(typeof(PathFinder), name) ??
            throw new MissingMethodException(typeof(PathFinder).FullName, name);

        private sealed class PathSpatialScratch
        {
            internal readonly HashSet<int> ExpandedCells = new(4096);
            internal readonly HashSet<int> Chunks8 = new(512);
            internal readonly HashSet<int> Chunks16 = new(256);
            internal readonly HashSet<int> Chunks32 = new(128);

            internal void Clear()
            {
                ExpandedCells.Clear();
                Chunks8.Clear();
                Chunks16.Clear();
                Chunks32.Clear();
            }
        }
    }
}
