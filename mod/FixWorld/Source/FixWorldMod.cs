using FixWorld.Caching;
using FixWorld.Diagnostics;
using FixWorld.Integration;
using FixWorld.Loading;
using HarmonyLib;
using Verse;

namespace FixWorld
{
    public sealed class FixWorldMod : Mod
    {
        private const string HarmonyId = "smolblackhole.fixworld";

        public FixWorldMod(ModContentPack content) : base(content)
        {
            TextureDdsCache.Initialize(content.RootDir);
            LoadingSession.Start();
            RimWorldHooks.Install(new Harmony(HarmonyId));
            Log.Message(
                "[FixWorld] Loaded. DDS cache and loading progress are active; benchmark=" +
                BenchmarkRecorder.Enabled + ".");
        }
    }
}
