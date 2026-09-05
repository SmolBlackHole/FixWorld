using System;
using FixWorld.Diagnostics;
using FixWorld.Runtime;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class RuntimeProfilingHooks
    {
        internal static readonly Type[] PatchTypes =
        [
            typeof(TickPatch),
            typeof(MapPreTickPatch),
            typeof(MapPostTickPatch)
        ];

        private static long Begin(RuntimeHotpath hotpath) =>
            RuntimeHost.StartRuntimeHotpath(hotpath);

        private static void End(RuntimeHotpath hotpath, long startedAt) =>
            RuntimeHost.StopRuntimeHotpath(hotpath, startedAt);

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
    }
}
