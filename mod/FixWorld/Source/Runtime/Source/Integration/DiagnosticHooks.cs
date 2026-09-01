using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using FixWorld.Diagnostics;
using HarmonyLib;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.Integration
{
    internal static class DiagnosticHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(DdsLoaderProbePatch),
            typeof(TextureLoaderProbePatch)
        };

        [HarmonyPatch(typeof(ModDdsLoader), nameof(ModDdsLoader.TryLoadDds))]
        private static class DdsLoaderProbePatch
        {
            [HarmonyPrefix]
            private static void Prefix(VirtualFile file, out long __state)
            {
                __state = TextureProbe.BeginDdsLoad(file);
            }

            [HarmonyPostfix]
            private static void Postfix(long __state)
            {
                TextureProbe.EndDdsLoad(__state);
            }
        }

        [HarmonyPatch]
        private static class TextureLoaderProbePatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(ModContentLoader<Texture2D>),
                    "LoadTextureViaImageConversion") ??
                       throw new MissingMethodException(
                           typeof(ModContentLoader<Texture2D>).FullName,
                           "LoadTextureViaImageConversion");
            }

            [HarmonyPrefix]
            private static void Prefix(out long __state)
            {
                __state = TextureProbe.BeginLoad();
            }

            [HarmonyPostfix]
            private static void Postfix(long __state)
            {
                TextureProbe.EndLoad(__state);
            }

            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo readOriginal = AccessTools.Method(
                    typeof(VirtualFile),
                    nameof(VirtualFile.ReadAllBytes));
                MethodInfo readReplacement = AccessTools.Method(
                    typeof(TextureProbe),
                    nameof(TextureProbe.ReadAllBytes));
                MethodInfo loadImageOriginal = AccessTools.Method(
                    typeof(ImageConversion),
                    nameof(ImageConversion.LoadImage),
                    new[] { typeof(Texture2D), typeof(byte[]) });
                MethodInfo loadImageReplacement = AccessTools.Method(
                    typeof(TextureProbe),
                    nameof(TextureProbe.LoadImage));
                MethodInfo applyOriginal = AccessTools.Method(
                    typeof(Texture2D),
                    nameof(Texture2D.Apply),
                    new[] { typeof(bool), typeof(bool) });
                MethodInfo applyReplacement = AccessTools.Method(
                    typeof(TextureProbe),
                    nameof(TextureProbe.Apply));
                MethodInfo fastCompressOriginal = AccessTools.Method(
                    typeof(StaticTextureAtlas),
                    nameof(StaticTextureAtlas.FastCompressDXT),
                    new[] { typeof(Texture2D), typeof(bool) });
                MethodInfo fastCompressReplacement = AccessTools.Method(
                    typeof(TextureProbe),
                    nameof(TextureProbe.FastCompressDXT));
                int readReplacements = 0;
                int loadImageReplacements = 0;
                int applyReplacements = 0;
                int fastCompressReplacements = 0;

                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(readOriginal))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = readReplacement;
                        readReplacements++;
                    }
                    else if (instruction.Calls(loadImageOriginal))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = loadImageReplacement;
                        loadImageReplacements++;
                    }
                    else if (instruction.Calls(applyOriginal))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = applyReplacement;
                        applyReplacements++;
                    }
                    else if (instruction.Calls(fastCompressOriginal))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = fastCompressReplacement;
                        fastCompressReplacements++;
                    }

                    yield return instruction;
                }

                if (readReplacements != 1 ||
                    loadImageReplacements != 2 ||
                    applyReplacements != 3 ||
                    fastCompressReplacements != 1)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Unexpected LoadTextureViaImageConversion call shape: " +
                            "read={0}, loadImage={1}, apply={2}, fastCompress={3}.",
                            readReplacements,
                            loadImageReplacements,
                            applyReplacements,
                            fastCompressReplacements));
                }
            }
        }
    }
}
