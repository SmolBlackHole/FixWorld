using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FixWorld.Runtime;
using HarmonyLib;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.Integration
{
    internal static class TextureHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(ModFileIndexPatch),
            typeof(TextureLoadPatch)
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
        private static class TextureLoadPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(ModContentLoader<Texture2D>),
                    "LoadTexture",
                    new[] { typeof(VirtualFile) }) ??
                    throw new MissingMethodException(
                        typeof(ModContentLoader<Texture2D>).FullName,
                        "LoadTexture");
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            private static bool Prefix(
                VirtualFile file,
                ref Texture2D __result)
            {
                return !RuntimeHost.Current.Textures.TryLoad(
                    file,
                    out __result);
            }
        }
    }
}
