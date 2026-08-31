using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using FixWorld.Caching;
using FixWorld.Diagnostics;
using FixWorld.Loading;
using FixWorld.Preloader;
using FixWorld.UI;
using HarmonyLib;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.Integration
{
    internal static class RimWorldHooks
    {
        private const string HarmonyId = "smolblackhole.fixworld";
        private static readonly object Sync = new object();
        private static readonly HashSet<Type> PatchedTypes = new HashSet<Type>();
        private static readonly Type[] RuntimePatchTypes =
        {
            typeof(ModFileLoaderPatch),
            typeof(DeepProfilerStartPatch),
            typeof(DeepProfilerEndPatch),
            typeof(DelayedInitializationPatch),
            typeof(EnumeratorFrameBoundaryPatch),
            typeof(LoaderCompletionPatch),
            typeof(LoadingOverlayPatch)
        };
        private static readonly Type[] DiagnosticPatchTypes =
        {
            typeof(DdsLoaderProbePatch),
            typeof(TextureLoaderProbePatch)
        };

        private static Harmony harmony;
        private static bool runtimeInstalled;
        private static bool diagnosticsInstalled;

        internal static bool Install(bool diagnosticsEnabled)
        {
            lock (Sync)
            {
                harmony = harmony ?? new Harmony(HarmonyId);
                try
                {
                    if (!runtimeInstalled)
                    {
                        PatchAll(RuntimePatchTypes);
                        runtimeInstalled = true;
                    }

                    if (diagnosticsEnabled && !diagnosticsInstalled)
                    {
                        PatchAll(DiagnosticPatchTypes);
                        diagnosticsInstalled = true;
                    }

                    return true;
                }
                catch (Exception exception)
                {
                    RollBack();
                    Log.Error("[FixWorld] Could not install RimWorld hooks: " + exception);
                    return false;
                }
            }
        }

        private static void PatchAll(IEnumerable<Type> patchTypes)
        {
            foreach (Type patchType in patchTypes)
            {
                if (PatchedTypes.Contains(patchType))
                {
                    continue;
                }

                harmony.CreateClassProcessor(patchType).Patch();
                PatchedTypes.Add(patchType);
            }
        }

        private static void RollBack()
        {
            try
            {
                harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception exception)
            {
                Log.Error("[FixWorld] Could not roll back RimWorld hooks: " + exception);
            }

            PatchedTypes.Clear();
            runtimeInstalled = false;
            diagnosticsInstalled = false;
        }

        [HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.GetAllFilesForMod))]
        private static class ModFileLoaderPatch
        {
            [HarmonyPrefix]
            private static void Prefix(out long __state)
            {
                __state = BenchmarkRecorder.BeginFileDiscovery();
            }

            [HarmonyPostfix]
            private static void Postfix(
                long __state,
                ModContentPack mod,
                string contentPath,
                List<string> foldersToLoadDebug,
                Dictionary<string, FileInfo> __result)
            {
                BenchmarkRecorder.ObserveFiles(__state, mod, contentPath, __result);
                TextureDdsCache.Apply(mod, contentPath, foldersToLoadDebug, __result);
            }
        }

        [HarmonyPatch(typeof(DeepProfiler), nameof(DeepProfiler.Start), new[] { typeof(string) })]
        private static class DeepProfilerStartPatch
        {
            [HarmonyPrefix]
            private static void Prefix(string label)
            {
                LoadingSession.Begin(label);
            }
        }

        [HarmonyPatch(typeof(DeepProfiler), nameof(DeepProfiler.End))]
        private static class DeepProfilerEndPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                LoadingSession.End();
            }
        }

        [HarmonyPatch]
        private static class DelayedInitializationPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(LongEventHandler),
                    "ExecuteToExecuteWhenFinished") ??
                       throw new MissingMethodException(
                           typeof(LongEventHandler).FullName,
                           "ExecuteToExecuteWhenFinished");
            }

            [HarmonyPrefix]
            private static bool Prefix()
            {
                return StagedLoadingRunner.ShouldRunOriginal();
            }
        }

        [HarmonyPatch]
        private static class EnumeratorFrameBoundaryPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(LongEventHandler),
                    "UpdateCurrentEnumeratorEvent") ??
                       throw new MissingMethodException(
                           typeof(LongEventHandler).FullName,
                           "UpdateCurrentEnumeratorEvent");
            }

            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions,
                ILGenerator generator)
            {
                List<CodeInstruction> rewritten = instructions.ToList();
                List<int> loopBranches = rewritten
                    .Select((instruction, index) => new { instruction, index })
                    .Where(item =>
                        item.instruction.opcode == OpCodes.Bgt_Un_S ||
                        item.instruction.opcode == OpCodes.Bgt_Un)
                    .Select(item => item.index)
                    .ToList();
                if (loopBranches.Count != 1 || loopBranches[0] + 1 >= rewritten.Count)
                {
                    Log.Error(
                        "[FixWorld] Could not add the staged-loader frame boundary; " +
                        "RimWorld's enumerator loop has an unexpected shape.");
                    return rewritten;
                }

                int branchIndex = loopBranches[0];
                CodeInstruction branch = rewritten[branchIndex];
                Label loopStart = (Label)branch.operand;
                Label exitLoop = generator.DefineLabel();
                rewritten[branchIndex + 1].labels.Add(exitLoop);

                CodeInstruction deadlinePassed = new CodeInstruction(
                    OpCodes.Ble_Un,
                    exitLoop);
                deadlinePassed.labels.AddRange(branch.labels);
                deadlinePassed.blocks.AddRange(branch.blocks);
                MethodInfo shouldStop = AccessTools.Method(
                    typeof(StagedLoadingRunner),
                    nameof(StagedLoadingRunner.ConsumeFrameBoundaryRequest));
                rewritten.RemoveAt(branchIndex);
                rewritten.InsertRange(
                    branchIndex,
                    new[]
                    {
                        deadlinePassed,
                        new CodeInstruction(OpCodes.Call, shouldStop),
                        new CodeInstruction(OpCodes.Brfalse, loopStart)
                    });
                return rewritten;
            }
        }

        [HarmonyPatch(typeof(AbstractFilesystem), nameof(AbstractFilesystem.ClearAllCache))]
        private static class LoaderCompletionPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!LoadingSession.TryComplete())
                {
                    return;
                }

                BenchmarkRecorder.Complete("play-data-clear-cache");
                Log.Message("[FixWorld] Main menu ready.");
                PreloaderPrompt.TryShow();
            }
        }

        [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
        private static class LoadingOverlayPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (LoadingSession.TryGetSnapshot(out LoadingSnapshot snapshot))
                {
                    LoadingProgressUi.Draw(snapshot);
                }
            }
        }

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
