using System.Reflection;
using HarmonyLib;
using RimWorldOptim.Poc.Caching;
using Verse;

namespace RimWorldOptim.Poc
{
    public sealed class RimWorldOptimPocMod : Mod
    {
        private const string HarmonyId = "local.rimworldoptim.poc";

        public RimWorldOptimPocMod(ModContentPack content) : base(content)
        {
            TextureDdsCache.Initialize(content.RootDir);
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[RimWorldOptim.Poc] Loaded. Automatic DDS cache and optional profilers are active.");
        }
    }
}
