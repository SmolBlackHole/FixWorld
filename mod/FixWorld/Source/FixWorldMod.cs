using System.Reflection;
using HarmonyLib;
using FixWorld.Caching;
using Verse;

namespace FixWorld
{
    public sealed class FixWorldMod : Mod
    {
        private const string HarmonyId = "smolblackhole.fixworld";

        public FixWorldMod(ModContentPack content) : base(content)
        {
            TextureDdsCache.Initialize(content.RootDir);
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[FixWorld] Loaded. Automatic DDS cache and optional profilers are active.");
        }
    }
}
