using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FixWorld.Diagnostics;
using FixWorld.Runtime;
using FixWorld.Textures;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class TextureHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(ModFileLoaderPatch)
        };

        [HarmonyPatch(
            typeof(ModContentPack),
            nameof(ModContentPack.GetAllFilesForMod))]
        private static class ModFileLoaderPatch
        {
            private static readonly MethodBase PatchedMethod = AccessTools.Method(
                typeof(ModContentPack),
                nameof(ModContentPack.GetAllFilesForMod));
            private static bool foreignPatchReported;

            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            private static bool Prefix(
                ModContentPack mod,
                string contentPath,
                Func<string, bool> validateExtension,
                List<string> foldersToLoadDebug,
                ref Dictionary<string, FileInfo> __result,
                out ModFileLoadState __state)
            {
                __state = new ModFileLoadState(
                    BenchmarkRecorder.BeginFileDiscovery());
                if (HasForeignPatches())
                {
                    ReportForeignPatchFallback();
                    return true;
                }

                try
                {
                    long discoveryStartedAt = __state.DiscoveryStartedAt;
                    __result = ModFileLoader.Discover(
                        mod,
                        contentPath,
                        validateExtension,
                        foldersToLoadDebug);

                    BenchmarkRecorder.ObserveFiles(
                        discoveryStartedAt,
                        mod,
                        contentPath,
                        __result);
                    RuntimeHost.Current.Textures.Apply(
                        mod,
                        contentPath,
                        foldersToLoadDebug,
                        __result);
                    __state.OwnedByFixWorld = true;
                    return false;
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[FixWorld] File discovery fell back to RimWorld for " +
                        mod.PackageId + ": " + exception);
                    return true;
                }
            }

            [HarmonyPostfix]
            private static void Postfix(
                ModFileLoadState __state,
                ModContentPack mod,
                string contentPath,
                List<string> foldersToLoadDebug,
                Dictionary<string, FileInfo> __result)
            {
                if (__state.OwnedByFixWorld)
                {
                    return;
                }

                BenchmarkRecorder.ObserveFiles(
                    __state.DiscoveryStartedAt,
                    mod,
                    contentPath,
                    __result);
                RuntimeHost.Current.Textures.Apply(
                    mod,
                    contentPath,
                    foldersToLoadDebug,
                    __result);
            }

            private static bool HasForeignPatches()
            {
                return HarmonyPatchInspector.Any(
                    PatchedMethod,
                    predicate: patch =>
                        !RimWorldHooks.IsFixWorldOwner(patch.owner));
            }

            private static void ReportForeignPatchFallback()
            {
                if (foreignPatchReported)
                {
                    return;
                }

                foreignPatchReported = true;
                Log.Warning(
                    "[FixWorld] Another mod patches " +
                    "ModContentPack.GetAllFilesForMod; FixWorld leaves file " +
                    "discovery to RimWorld and only applies the DDS cache.");
            }

            private struct ModFileLoadState
            {
                internal readonly long DiscoveryStartedAt;
                internal bool OwnedByFixWorld;

                internal ModFileLoadState(long discoveryStartedAt)
                {
                    DiscoveryStartedAt = discoveryStartedAt;
                    OwnedByFixWorld = false;
                }
            }
        }
    }
}
