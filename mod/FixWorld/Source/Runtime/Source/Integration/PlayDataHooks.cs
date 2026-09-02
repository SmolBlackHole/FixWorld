using System;
using System.Reflection;
using FixWorld.Runtime;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class PlayDataHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(DoPlayLoadPatch),
            typeof(CaptureDeferredWorkPatch)
        };

        [HarmonyPatch]
        private static class DoPlayLoadPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(PlayDataLoader), "DoPlayLoad") ??
                       throw new MissingMethodException(
                           typeof(PlayDataLoader).FullName,
                           "DoPlayLoad");
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                RuntimeHost.RunPlayData();
                return false;
            }
        }

        [HarmonyPatch(
            typeof(LongEventHandler),
            nameof(LongEventHandler.ExecuteWhenFinished))]
        private static class CaptureDeferredWorkPatch
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Action action)
            {
                return !RuntimeHost.TryCaptureDeferred(action);
            }
        }
    }
}
