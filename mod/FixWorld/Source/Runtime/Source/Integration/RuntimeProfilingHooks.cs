using System;
using System.Collections.Generic;
using System.Reflection;
using FixWorld.Diagnostics;
using FixWorld.Runtime;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace FixWorld.Integration
{
    internal static class RuntimeProfilingHooks
    {
        internal static readonly Type[] PatchTypes =
        [
            typeof(TickPatch),
            typeof(MapPreTickPatch),
            typeof(MapPostTickPatch),
            typeof(PathFinderTickPatch),
            typeof(PathFinderPushRequestPatch),
            typeof(PathFinderFindPathNowPatch),
            typeof(PathFinderGatherMapDataPatch),
            typeof(PathFinderDataSourcePatch),
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
                out HotpathState __state)
            {
                if (__instance is AreaSource)
                {
                    RuntimeHost.ObservePathDataUpdate(__1.Count);
                }

                __state = new HotpathState(
                    GetDataSourceHotpath(__instance));
            }

            [HarmonyPostfix]
            private static void Postfix(HotpathState __state) =>
                End(__state.Hotpath, __state.StartedAt);
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
                __state = Begin(RuntimeHotpath.PathFinderPathScheduling);
            }

            [HarmonyPostfix]
            private static void Postfix(long __state) =>
                End(RuntimeHotpath.PathFinderPathScheduling, __state);
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
    }
}
