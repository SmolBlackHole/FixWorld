using System;
using FixWorld.Runtime;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class BootstrapHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(LoadAllPlayDataPatch)
        };

        [HarmonyPatch(
            typeof(PlayDataLoader),
            nameof(PlayDataLoader.LoadAllPlayData))]
        private static class LoadAllPlayDataPatch
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static void Prefix()
            {
                if (!RuntimeHost.ActivateRuntimeHooks())
                {
                    Log.Error(
                        "[FixWorld] Runtime hooks could not be activated; " +
                        "RimWorld will use its original play-data loader.");
                }
            }
        }
    }
}
