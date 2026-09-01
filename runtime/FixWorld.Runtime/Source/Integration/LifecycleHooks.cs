using System;
using FixWorld.Lifecycle;
using FixWorld.Loading;
using FixWorld.Runtime;
using FixWorld.Scheduling;
using HarmonyLib;
using RimWorld;
using RimWorld.IO;
using Verse;

namespace FixWorld.Integration
{
    internal static class LifecycleHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(RuntimePumpPatch),
            typeof(RuntimeShutdownPatch),
            typeof(PlayDataReadyPatch),
            typeof(MainMenuReadyPatch),
            typeof(GameEndedPatch)
        };

        [HarmonyPatch(typeof(Root), nameof(Root.Update))]
        private static class RuntimePumpPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                FixWorldScheduler.BindMainThread();
                FixWorldScheduler.PumpMainThread();
                RimWorldLifecycle.ObserveFrame();
                FixWorldEvents.Pump();
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

        [HarmonyPatch(typeof(AbstractFilesystem), nameof(AbstractFilesystem.ClearAllCache))]
        private static class PlayDataReadyPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!VanillaDelayedActionBridge.IsRunning)
                {
                    RimWorldLifecycle.NotifyPlayDataReady(
                        "play-data-clear-cache");
                }
            }
        }

        [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
        private static class MainMenuReadyPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RimWorldLifecycle.NotifyMainMenuReady();
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
        private static class GameEndedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Game __instance)
            {
                RimWorldLifecycle.NotifyGameEnded(__instance);
            }
        }
    }
}
