using HarmonyLib;
using RimWorld;
using FixWorld.Caching;
using FixWorld.Profiling;
using Verse;

namespace FixWorld.Patches
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
            Log.Message("[FixWorld] Main menu ready.");
        }
    }
}
