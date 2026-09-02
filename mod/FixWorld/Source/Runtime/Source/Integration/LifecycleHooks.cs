using System;
using FixWorld.Runtime;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FixWorld.Integration
{
    internal static class LifecycleHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(RuntimePumpPatch),
            typeof(RuntimeShutdownPatch),
            typeof(MainMenuReadyPatch),
            typeof(GameEndedPatch)
        };

        [HarmonyPatch(typeof(Root), nameof(Root.Update))]
        private static class RuntimePumpPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.Pump();
            }
        }

        [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
        private static class RuntimeShutdownPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                FixWorldRuntime.Shutdown();
            }
        }

        [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
        private static class MainMenuReadyPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.NotifyMainMenuReady();
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
        private static class GameEndedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Game __instance)
            {
                RuntimeHost.NotifyGameEnded(__instance);
            }
        }
    }
}
