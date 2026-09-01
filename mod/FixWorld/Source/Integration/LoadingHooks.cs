using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using FixWorld.Diagnostics;
using FixWorld.Loading;
using FixWorld.Textures;
using FixWorld.UI;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FixWorld.Integration
{
    internal static class LoadingHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(ModFileLoaderPatch),
            typeof(DeepProfilerStartPatch),
            typeof(DeepProfilerEndPatch),
            typeof(XmlLoadingPatch),
            typeof(PatchLoadingPatch),
            typeof(DelayedInitializationPatch),
            typeof(EnumeratorFrameBoundaryPatch),
            typeof(LoadingOverlayPatch)
        };

        private static bool IsFixWorldOwner(string owner)
        {
            return RimWorldHooks.IsFixWorldOwner(owner);
        }

        [HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.GetAllFilesForMod))]
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
                __state = new ModFileLoadState(BenchmarkRecorder.BeginFileDiscovery());
                if (HasForeignPatches())
                {
                    ReportForeignPatchFallback();
                    return true;
                }

                try
                {
                    long discoveryStartedAt = __state.DiscoveryStartedAt;
                    __result = ModFileLoader.Load(
                        mod,
                        contentPath,
                        validateExtension,
                        foldersToLoadDebug,
                        files => BenchmarkRecorder.ObserveFiles(
                            discoveryStartedAt,
                            mod,
                            contentPath,
                            files));
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
                if (!__state.OwnedByFixWorld)
                {
                    BenchmarkRecorder.ObserveFiles(
                        __state.DiscoveryStartedAt,
                        mod,
                        contentPath,
                        __result);
                    TextureDdsCache.Apply(mod, contentPath, foldersToLoadDebug, __result);
                }
            }

            private static bool HasForeignPatches()
            {
                Patches patches = Harmony.GetPatchInfo(PatchedMethod);
                return patches != null &&
                       patches.Prefixes
                           .Concat(patches.Postfixes)
                           .Concat(patches.Transpilers)
                           .Concat(patches.Finalizers)
                           .Any(patch => !IsFixWorldOwner(patch.owner));
            }

            private static void ReportForeignPatchFallback()
            {
                if (foreignPatchReported)
                {
                    return;
                }

                foreignPatchReported = true;
                Log.Warning(
                    "[FixWorld] Another mod patches ModContentPack.GetAllFilesForMod; " +
                    "FixWorld leaves file discovery to RimWorld and only applies the DDS cache.");
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

        [HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.LoadModXML))]
        private static class XmlLoadingPatch
        {
            private static readonly MethodInfo LoadModXmlMethod = AccessTools.Method(
                typeof(LoadedModManager),
                nameof(LoadedModManager.LoadModXML),
                new[] { typeof(bool) });
            private static readonly MethodInfo LoadDefsMethod = AccessTools.Method(
                typeof(ModContentPack),
                nameof(ModContentPack.LoadDefs),
                new[] { typeof(bool) });
            private static readonly MethodInfo XmlAssetsMethod = AccessTools.Method(
                typeof(DirectXmlLoader),
                nameof(DirectXmlLoader.XmlAssetsInModFolder),
                new[]
                {
                    typeof(ModContentPack),
                    typeof(string),
                    typeof(List<string>)
                });
            private static bool foreignPatchReported;

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(
                bool hotReload,
                ref List<LoadableXmlAsset> __result)
            {
                if (TryGetForeignOwners(out string owners))
                {
                    XmlLoadingPipeline.RecordOriginalFallback(
                        hotReload,
                        "foreign Harmony patches: " + owners);
                    if (!foreignPatchReported)
                    {
                        foreignPatchReported = true;
                        Log.Warning(
                            "[FixWorld] XML loading remains with RimWorld because " +
                            "another mod patches its XML contract: " + owners + ".");
                    }

                    return true;
                }

                try
                {
                    __result = XmlLoadingPipeline.Run(hotReload);
                    return false;
                }
                catch (Exception exception)
                {
                    XmlLoadingPipeline.RecordOriginalFallback(
                        hotReload,
                        "FixWorld XML pipeline failed");
                    Log.Warning(
                        "[FixWorld] XML loading fell back to RimWorld: " + exception);
                    return true;
                }
            }

            private static bool TryGetForeignOwners(out string owners)
            {
                HashSet<string> foreignOwners = new HashSet<string>(
                    StringComparer.Ordinal);
                CollectForeignOwners(LoadModXmlMethod, foreignOwners);
                CollectForeignOwners(LoadDefsMethod, foreignOwners);
                CollectForeignOwners(XmlAssetsMethod, foreignOwners);

                IteratorStateMachineAttribute iterator =
                    LoadDefsMethod?.GetCustomAttribute<IteratorStateMachineAttribute>();
                MethodInfo moveNext = iterator?.StateMachineType.GetMethod(
                    "MoveNext",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                CollectForeignOwners(moveNext, foreignOwners);

                owners = string.Join(", ", foreignOwners.OrderBy(item => item));
                return foreignOwners.Count > 0;
            }

            private static void CollectForeignOwners(
                MethodBase method,
                ISet<string> owners)
            {
                if (method == null)
                {
                    return;
                }

                Patches patches = Harmony.GetPatchInfo(method);
                if (patches == null)
                {
                    return;
                }

                foreach (Patch patch in patches.Prefixes
                             .Concat(patches.Postfixes)
                             .Concat(patches.Transpilers)
                             .Concat(patches.Finalizers))
                {
                    if (!IsFixWorldOwner(patch.owner))
                    {
                        owners.Add(patch.owner);
                    }
                }
            }
        }

        [HarmonyPatch]
        private static class PatchLoadingPatch
        {
            private static readonly Guid CompatibleModSettingsFrameworkMvid =
                new Guid("1190b201-8e2b-4c34-9d77-d6756e7177af");
            private static readonly MethodInfo CheckPatchesMethod = AccessTools.Method(
                typeof(LoadedModManager),
                nameof(LoadedModManager.ErrorCheckPatches));
            private static readonly MethodInfo ApplyPatchesMethod = AccessTools.Method(
                typeof(LoadedModManager),
                nameof(LoadedModManager.ApplyPatches),
                new[]
                {
                    typeof(System.Xml.XmlDocument),
                    typeof(Dictionary<System.Xml.XmlNode, LoadableXmlAsset>)
                });
            private static readonly HashSet<string> ReportedForeignOwners =
                new HashSet<string>(StringComparer.Ordinal);

            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return CheckPatchesMethod ??
                             throw new MissingMethodException(
                                 typeof(LoadedModManager).FullName,
                                 nameof(LoadedModManager.ErrorCheckPatches));
                yield return ApplyPatchesMethod ??
                             throw new MissingMethodException(
                                 typeof(LoadedModManager).FullName,
                                 nameof(LoadedModManager.ApplyPatches));
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(
                MethodBase __originalMethod,
                object[] __args,
                out LoadingOperation __state)
            {
                __state = null;
                if (TryGetForeignOwners(__originalMethod, out string owners))
                {
                    ReportForeignOwners(__originalMethod, owners);
                    __state = PatchLoadingPipeline.BeginOriginal(
                        GetStep(__originalMethod),
                        GetDisplayName(__originalMethod),
                        GetActivity(__originalMethod));
                    return true;
                }

                if (__originalMethod == CheckPatchesMethod)
                {
                    PatchLoadingPipeline.Check();
                }
                else
                {
                    PatchLoadingPipeline.Apply((System.Xml.XmlDocument)__args[0]);
                }

                return false;
            }

            [HarmonyPostfix]
            private static void Postfix(LoadingOperation __state)
            {
                __state?.Dispose();
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(
                Exception __exception,
                LoadingOperation __state)
            {
                if (__exception != null)
                {
                    __state?.Fail();
                }

                __state?.Dispose();
                return __exception;
            }

            private static bool TryGetForeignOwners(
                MethodBase method,
                out string owners)
            {
                Patches patches = Harmony.GetPatchInfo(method);
                string[] foreignOwners = patches == null
                    ? Array.Empty<string>()
                    : patches.Prefixes
                        .Where(patch => !IsCompatiblePrefix(method, patch))
                        .Concat(patches.Postfixes)
                        .Concat(patches.Transpilers)
                        .Concat(patches.Finalizers)
                        .Where(patch => !IsFixWorldOwner(patch.owner))
                        .Select(patch => patch.owner)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(owner => owner)
                        .ToArray();
                owners = string.Join(", ", foreignOwners);
                return foreignOwners.Length > 0;
            }

            private static bool IsCompatiblePrefix(MethodBase method, Patch patch)
            {
                MethodInfo patchMethod = patch.PatchMethod;
                return method == ApplyPatchesMethod &&
                       string.Equals(
                           patch.owner,
                           "ModSettingsFrameworkMod",
                           StringComparison.Ordinal) &&
                       string.Equals(
                           patchMethod?.DeclaringType?.FullName,
                           "ModSettingsFramework.LoadedModManager_ApplyPatches_Patch",
                           StringComparison.Ordinal) &&
                       string.Equals(
                           patchMethod.Name,
                           "Prefix",
                           StringComparison.Ordinal) &&
                       patchMethod.ReturnType == typeof(void) &&
                       patchMethod.GetParameters().Length == 0 &&
                       patchMethod.Module.ModuleVersionId ==
                       CompatibleModSettingsFrameworkMvid;
            }

            private static void ReportForeignOwners(MethodBase method, string owners)
            {
                string key = method.Name + ":" + owners;
                if (!ReportedForeignOwners.Add(key))
                {
                    return;
                }

                Log.Warning(
                    "[FixWorld] Patch processing remains with RimWorld because " +
                    "another mod patches " + method.Name + ": " + owners + ".");
            }

            private static LoadingStep GetStep(MethodBase method)
            {
                return method == CheckPatchesMethod
                    ? LoadingStep.CheckPatches
                    : LoadingStep.ApplyPatches;
            }

            private static string GetDisplayName(MethodBase method)
            {
                return method == CheckPatchesMethod
                    ? "Check patches"
                    : "Apply patches";
            }

            private static string GetActivity(MethodBase method)
            {
                return method == CheckPatchesMethod
                    ? "RimWorld is checking mod patches"
                    : "RimWorld is applying mod patches";
            }
        }

        [HarmonyPatch(typeof(DeepProfiler), nameof(DeepProfiler.Start), new[] { typeof(string) })]
        private static class DeepProfilerStartPatch
        {
            [HarmonyPrefix]
            private static void Prefix(string label)
            {
                LoadingTelemetry.BeginProfiler(label);
            }
        }

        [HarmonyPatch(typeof(DeepProfiler), nameof(DeepProfiler.End))]
        private static class DeepProfilerEndPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                LoadingTelemetry.EndProfiler();
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
                    typeof(LoadingScheduler),
                    nameof(LoadingScheduler.ConsumeFrameBoundaryRequest));
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

    }
}
