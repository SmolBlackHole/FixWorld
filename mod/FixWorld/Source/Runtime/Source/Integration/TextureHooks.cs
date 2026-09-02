using System;
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
            typeof(TextureLoadPatch)
        };

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
