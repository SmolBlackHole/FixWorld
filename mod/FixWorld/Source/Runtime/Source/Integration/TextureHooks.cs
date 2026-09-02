using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FixWorld.Runtime;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace FixWorld.Integration
{
    internal static class TextureHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(ModFileIndexPatch),
            typeof(TextureContentPatch)
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
                        "[FixWorld] Indexed file lookup fell back to RimWorld " +
                        "for " + mod.PackageId + ": " + exception);
                    return true;
                }
            }
        }

        [HarmonyPatch]
        private static class TextureContentPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(ModContentLoader<Texture2D>),
                    nameof(ModContentLoader<Texture2D>.LoadAllForMod));
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            private static bool Prefix(
                ModContentPack mod,
                ref IEnumerable<Pair<
                    string,
                    LoadedContentItem<Texture2D>>> __result)
            {
                RuntimeContext context = RuntimeHost.Current;
                __result = context.Textures.LoadAll(mod, context.ModFiles);
                return false;
            }
        }
    }
}
