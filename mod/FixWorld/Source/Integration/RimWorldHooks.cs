using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FixWorld.Caching;
using FixWorld.Diagnostics;
using FixWorld.Loading;
using FixWorld.UI;
using HarmonyLib;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.Integration
{
    internal static class RimWorldHooks
    {
        internal static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(ModFileLoaderPatch));
            Patch(harmony, typeof(DeepProfilerStartPatch));
            Patch(harmony, typeof(DeepProfilerEndPatch));
            Patch(harmony, typeof(LoaderCompletionPatch));
            Patch(harmony, typeof(LoadingWindowPatch));

            if (BenchmarkRecorder.Enabled)
            {
                Patch(harmony, typeof(DdsLoaderProbePatch));
                Patch(harmony, typeof(TextureLoaderProbePatch));
            }
        }

        private static void Patch(Harmony harmony, Type patchType)
        {
            harmony.CreateClassProcessor(patchType).Patch();
        }
    }

    [HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.GetAllFilesForMod))]
    internal static class ModFileLoaderPatch
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
    internal static class DeepProfilerStartPatch
    {
        [HarmonyPrefix]
        private static void Prefix(string label)
        {
            LoadingSession.Begin(label);
        }
    }

    [HarmonyPatch(typeof(DeepProfiler), nameof(DeepProfiler.End))]
    internal static class DeepProfilerEndPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            LoadingSession.End();
        }
    }

    [HarmonyPatch(typeof(AbstractFilesystem), nameof(AbstractFilesystem.ClearAllCache))]
    internal static class LoaderCompletionPatch
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
        }
    }

    [HarmonyPatch]
    internal static class LoadingWindowPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                       typeof(LongEventHandler),
                       "DrawLongEventWindowContents",
                       new[] { typeof(Rect) }) ??
                   throw new MissingMethodException(
                       typeof(LongEventHandler).FullName,
                       "DrawLongEventWindowContents");
        }

        [HarmonyPrefix]
        private static bool Prefix(Rect rect)
        {
            if (!LoadingSession.TryGetSnapshot(out LoadingSnapshot snapshot))
            {
                return true;
            }

            LoadingProgressUi.Draw(rect, snapshot);
            return false;
        }
    }
}
