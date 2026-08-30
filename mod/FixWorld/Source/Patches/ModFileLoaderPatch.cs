using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using FixWorld.Caching;
using FixWorld.Profiling;
using Verse;

namespace FixWorld.Patches
{
    [HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.GetAllFilesForMod))]
    internal static class ModFileLoaderPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out long __state)
        {
            __state = FileDiscoveryProfiler.Begin();
        }

        [HarmonyPostfix]
        private static void Postfix(
            long __state,
            ModContentPack mod,
            string contentPath,
            List<string> foldersToLoadDebug,
            Dictionary<string, FileInfo> __result)
        {
            FileDiscoveryProfiler.End(__state, contentPath, __result);
            TexturePathProfiler.Observe(mod, contentPath, __result);
            TextureDdsCache.Apply(mod, contentPath, foldersToLoadDebug, __result);
        }
    }
}
