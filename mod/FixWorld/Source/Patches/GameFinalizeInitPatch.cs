using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    internal static class GameFinalizeInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            Log.Message("[FixWorld] Observed Game.FinalizeInit. Game state was not changed.");
        }
    }
}
