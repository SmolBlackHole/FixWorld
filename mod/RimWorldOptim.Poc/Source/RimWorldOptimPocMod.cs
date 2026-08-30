using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldOptim.Poc
{
    public sealed class RimWorldOptimPocMod : Mod
    {
        private const string HarmonyId = "local.rimworldoptim.poc";

        public RimWorldOptimPocMod(ModContentPack content) : base(content)
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[RimWorldOptim.Poc] Loaded. No optimization patches are active.");
        }
    }
}
