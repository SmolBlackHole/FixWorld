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
            typeof(TextureLoadPatch),
            typeof(TextureFileDiscoveryPatch)
        };

        [HarmonyPatch(
            typeof(ModContentPack),
            nameof(ModContentPack.GetAllFilesForMod))]
        private static class TextureFileDiscoveryPatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                ModContentPack mod,
                string contentPath,
                Func<string, bool> validateExtension,
                List<string> foldersToLoadDebug,
                Dictionary<string, FileInfo> __result)
            {
                RuntimeHost.Current.Textures.ObserveTextureFiles(
                    mod,
                    contentPath,
                    validateExtension,
                    foldersToLoadDebug,
                    __result);
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
