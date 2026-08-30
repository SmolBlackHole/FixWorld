using HarmonyLib;
using RimWorld;
using RimWorldOptim.Poc.Caching;
using RimWorldOptim.Poc.Profiling;
using Verse;

namespace RimWorldOptim.Poc.Patches
{
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class MainMenuReadyPatch
    {
        private static bool reported;

        [HarmonyPrefix]
        private static void Prefix()
        {
            if (reported)
            {
                return;
            }

            reported = true;
            ProfilerRegistry.WriteSummaries();
            TextureDdsCache.WriteSummary();
            Log.Message("[RimWorldOptim.Poc] Main menu ready.");
        }
    }
}
