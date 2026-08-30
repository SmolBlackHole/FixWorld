using HarmonyLib;
using Verse;

namespace RimWorldOptim.Poc.Patches
{
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    internal static class GameFinalizeInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            Log.Message("[RimWorldOptim.Poc] Harmony PoC observed Game.FinalizeInit. Game state was not changed.");
        }
    }
}
