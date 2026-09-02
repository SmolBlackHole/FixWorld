using System;
using System.Collections.Generic;
using System.IO;
using FixWorld.Runtime;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class ContentHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(ModFileIndexPatch),
            typeof(OrderedModFileIndexPatch)
        };

        [HarmonyPatch(
            typeof(ModContentPack),
            nameof(ModContentPack.GetAllFilesForMod))]
        private static class ModFileIndexPatch
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            private static bool Prefix(
                ModContentPack mod,
                string contentPath,
                Func<string, bool> validateExtension,
                List<string> foldersToLoadDebug,
                ref Dictionary<string, FileInfo> __result)
            {
                try
                {
                    __result = RuntimeHost.Current.ModFiles.GetFiles(
                        mod,
                        contentPath,
                        validateExtension,
                        foldersToLoadDebug);
                    return false;
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[FixWorld] Indexed file lookup fell back to " +
                        "RimWorld for " + mod.PackageId + ": " + exception);
                    return true;
                }
            }
        }

        [HarmonyPatch(
            typeof(ModContentPack),
            nameof(ModContentPack.GetAllFilesForModPreserveOrder))]
        private static class OrderedModFileIndexPatch
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            private static bool Prefix(
                ModContentPack mod,
                string contentPath,
                Func<string, bool> validateExtension,
                List<string> foldersToLoadDebug,
                ref List<Tuple<string, FileInfo>> __result)
            {
                try
                {
                    __result = RuntimeHost.Current.ModFiles
                        .GetFilesPreserveOrder(
                            mod,
                            contentPath,
                            validateExtension,
                            foldersToLoadDebug);
                    return false;
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[FixWorld] Ordered file lookup fell back to " +
                        "RimWorld for " + mod.PackageId + ": " + exception);
                    return true;
                }
            }
        }

    }
}
