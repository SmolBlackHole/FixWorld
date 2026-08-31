using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        private static readonly object Sync = new object();
        private static readonly HashSet<Type> PatchedTypes = new HashSet<Type>();
        private static readonly Type[] RuntimePatchTypes =
        {
            typeof(ModAssemblyReloadPatch),
            typeof(ModAssemblyFilesPatch),
            typeof(ModAssemblyReflectionPatch),
            typeof(ModFileLoaderPatch),
            typeof(ModContentItemLoadPatch),
            typeof(DeepProfilerStartPatch),
            typeof(DeepProfilerEndPatch),
            typeof(LoaderCompletionPatch),
            typeof(LoadingOverlayPatch)
        };

        private static readonly Type[] BenchmarkPatchTypes =
        {
            typeof(DdsLoaderProbePatch),
            typeof(TextureLoaderProbePatch)
        };

        internal static void InstallRuntime(Harmony harmony)
        {
            lock (Sync)
            {
                PatchAll(harmony, RuntimePatchTypes);

                if (BenchmarkRecorder.Enabled)
                {
                    PatchAll(harmony, BenchmarkPatchTypes);
                }
            }
        }

        private static void PatchAll(Harmony harmony, IEnumerable<Type> patchTypes)
        {
            foreach (Type patchType in patchTypes)
            {
                Patch(harmony, patchType);
            }
        }

        private static void Patch(Harmony harmony, Type patchType)
        {
            if (PatchedTypes.Contains(patchType))
            {
                return;
            }

            harmony.CreateClassProcessor(patchType).Patch();
            PatchedTypes.Add(patchType);
        }
    }

    [HarmonyPatch(typeof(ModAssemblyHandler), nameof(ModAssemblyHandler.ReloadAll))]
    internal static class ModAssemblyReloadPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ModContentPack ___mod)
        {
            LoadingSession.BeginModAssemblies(___mod);
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo loadFrom = AccessTools.Method(
                typeof(Assembly),
                nameof(Assembly.LoadFrom),
                new[] { typeof(string) });
            MethodInfo measuredLoadFrom = AccessTools.Method(
                typeof(ModAssemblyReloadPatch),
                nameof(LoadFromMeasured));
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(loadFrom))
                {
                    instruction.operand = measuredLoadFrom;
                    replacements++;
                }

                yield return instruction;
            }

            if (replacements != 1)
            {
                Log.Warning(
                    "[FixWorld] Expected one Assembly.LoadFrom call in " +
                    "ModAssemblyHandler.ReloadAll, found " + replacements + ".");
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            LoadingSession.EndModAssemblies();
            return __exception;
        }

        private static Assembly LoadFromMeasured(string path)
        {
            long startedAt = LoadingSession.BeginAssemblyFileLoad(path);
            bool loaded = false;
            try
            {
                Assembly assembly = Assembly.LoadFrom(path);
                loaded = true;
                return assembly;
            }
            finally
            {
                LoadingSession.EndAssemblyFileLoad(startedAt, loaded);
            }
        }
    }

    [HarmonyPatch(
        typeof(ModContentPack),
        nameof(ModContentPack.GetAllFilesForModPreserveOrder))]
    internal static class ModAssemblyFilesPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ModContentPack mod,
            string contentPath,
            List<Tuple<string, FileInfo>> __result)
        {
            if (string.Equals(contentPath, "Assemblies/", StringComparison.Ordinal))
            {
                LoadingSession.SetCurrentModAssemblyTotal(mod, __result?.Count ?? 0);
            }
        }
    }

    [HarmonyPatch]
    internal static class ModAssemblyReflectionPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ModAssemblyHandler), "AssemblyIsUsable") ??
                   throw new MissingMethodException(
                       typeof(ModAssemblyHandler).FullName,
                       "AssemblyIsUsable");
        }

        [HarmonyPrefix]
        private static void Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        [HarmonyPostfix]
        private static void Postfix(Assembly asm, bool __result, long __state)
        {
            LoadingSession.ObserveAssemblyReflection(asm, __state, __result);
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

            if (TryGetContentKind(contentPath, out LoadingContentKind kind))
            {
                LoadingSession.SetCurrentModItemTotal(
                    kind,
                    CountLoadedItems(kind, __result));
            }
        }

        private static bool TryGetContentKind(
            string contentPath,
            out LoadingContentKind kind)
        {
            if (string.Equals(contentPath, GenFilePaths.TexturesFolder, StringComparison.Ordinal))
            {
                kind = LoadingContentKind.Textures;
                return true;
            }

            if (string.Equals(contentPath, GenFilePaths.SoundsFolder, StringComparison.Ordinal))
            {
                kind = LoadingContentKind.Audio;
                return true;
            }

            if (string.Equals(contentPath, GenFilePaths.StringsFolder, StringComparison.Ordinal))
            {
                kind = LoadingContentKind.Strings;
                return true;
            }

            kind = LoadingContentKind.None;
            return false;
        }

        private static int CountLoadedItems(
            LoadingContentKind kind,
            Dictionary<string, FileInfo> files)
        {
            if (kind != LoadingContentKind.Textures)
            {
                return files.Count;
            }

            HashSet<string> ddsFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in files.Keys)
            {
                string normalized = path.ToLowerInvariant();
                if (normalized.EndsWith(".dds", StringComparison.Ordinal))
                {
                    ddsFiles.Add(normalized);
                }
            }

            int count = 0;
            foreach (string path in files.Keys)
            {
                string normalized = path.ToLowerInvariant();
                if (normalized.Length > 4 &&
                    !normalized.EndsWith(".dds", StringComparison.Ordinal) &&
                    ddsFiles.Contains(normalized.Substring(0, normalized.Length - 4) + ".dds"))
                {
                    continue;
                }

                count++;
            }

            return count;
        }
    }

    [HarmonyPatch]
    internal static class ModContentItemLoadPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(
                typeof(ModContentLoader<Texture2D>),
                nameof(ModContentLoader<Texture2D>.LoadItem));
            yield return AccessTools.Method(
                typeof(ModContentLoader<AudioClip>),
                nameof(ModContentLoader<AudioClip>.LoadItem));
            yield return AccessTools.Method(
                typeof(ModContentLoader<string>),
                nameof(ModContentLoader<string>.LoadItem));
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            LoadingSession.AdvanceCurrentModItem();
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
            PreloaderPrompt.TryShow();
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
    internal static class LoadingOverlayPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (!LoadingSession.TryGetSnapshot(out LoadingSnapshot snapshot))
            {
                return;
            }

            LoadingProgressUi.Draw(snapshot);
        }
    }
}
