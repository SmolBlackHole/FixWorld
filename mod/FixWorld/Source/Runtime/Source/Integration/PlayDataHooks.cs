using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using FixWorld.PlayData;
using FixWorld.Runtime;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FixWorld.Integration
{
    internal static class PlayDataHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(DoPlayLoadPatch),
            typeof(InitializeModsPatch),
            typeof(LoadModContentPatch),
            typeof(CreateModClassesPatch),
            typeof(LoadModXmlPatch),
            typeof(ParseDefinitionsPatch),
            typeof(CrossReferencePatch),
            typeof(PreResolveImpliedPatch),
            typeof(ResolveDefinitionsPatch),
            typeof(PostResolveImpliedPatch),
            typeof(FinalizeDefinitionsPatch),
            typeof(InitializeRuntimePatch),
            typeof(DeferredFramePumpPatch),
            typeof(DeferredWorkPatch)
        };

        [HarmonyPatch]
        private static class DoPlayLoadPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(PlayDataLoader), "DoPlayLoad") ??
                       throw new MissingMethodException(
                           typeof(PlayDataLoader).FullName,
                           "DoPlayLoad");
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static void Prefix()
            {
                RuntimeHost.BeginPlayData();
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    RuntimeHost.FailPlayData(__exception);
                }

                return __exception;
            }
        }

        [HarmonyPatch(
            typeof(LoadedModManager),
            nameof(LoadedModManager.InitializeMods))]
        private static class InitializeModsPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.TransitionStage(PlayDataLoadStage.InitializeMods);
            }

            [HarmonyPostfix]
            private static void Postfix()
            {
                RuntimeHost.PrepareTextures();
            }
        }

        [HarmonyPatch(
            typeof(LoadedModManager),
            nameof(LoadedModManager.LoadModContent))]
        private static class LoadModContentPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.TransitionStage(PlayDataLoadStage.PrepareModContent);
            }
        }

        [HarmonyPatch(
            typeof(LoadedModManager),
            nameof(LoadedModManager.CreateModClasses))]
        private static class CreateModClassesPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.TransitionStage(PlayDataLoadStage.CreateModClasses);
            }
        }

        [HarmonyPatch(
            typeof(LoadedModManager),
            nameof(LoadedModManager.LoadModXML))]
        private static class LoadModXmlPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.TransitionStage(PlayDataLoadStage.LoadAndPatchXml);
            }
        }

        [HarmonyPatch(
            typeof(LoadedModManager),
            nameof(LoadedModManager.ParseAndProcessXML))]
        private static class ParseDefinitionsPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.TransitionStage(PlayDataLoadStage.ImportDefinitions);
            }
        }

        [HarmonyPatch(
            typeof(DirectXmlCrossRefLoader),
            nameof(DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences))]
        private static class CrossReferencePatch
        {
            [HarmonyPrefix]
            private static void Prefix(FailMode failReportMode)
            {
                RuntimeHost.TransitionStage(
                    failReportMode == FailMode.Silent
                        ? PlayDataLoadStage.EarlyBinding
                        : PlayDataLoadStage.CrossReferenceResolution);
            }
        }

        [HarmonyPatch(
            typeof(DefGenerator),
            nameof(DefGenerator.GenerateImpliedDefs_PreResolve))]
        private static class PreResolveImpliedPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.TransitionStage(
                    PlayDataLoadStage.PreResolveImpliedDefinitions);
            }
        }

        [HarmonyPatch]
        private static class DeferredFramePumpPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                           typeof(LongEventHandler),
                           "UpdateCurrentAsynchronousEvent") ??
                       throw new MissingMethodException(
                           typeof(LongEventHandler).FullName,
                           "UpdateCurrentAsynchronousEvent");
            }

            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions,
                ILGenerator generator)
            {
                MethodInfo execute = AccessTools.Method(
                    typeof(LongEventHandler),
                    "ExecuteToExecuteWhenFinished") ??
                    throw new MissingMethodException(
                        typeof(LongEventHandler).FullName,
                        "ExecuteToExecuteWhenFinished");
                FieldInfo currentEvent = AccessTools.Field(
                    typeof(LongEventHandler),
                    "currentEvent") ??
                    throw new MissingFieldException(
                        typeof(LongEventHandler).FullName,
                        "currentEvent");
                MethodInfo begin = AccessTools.Method(
                    typeof(DeferredWorkPump),
                    nameof(DeferredWorkPump.TryBegin)) ??
                    throw new MissingMethodException(
                        typeof(DeferredWorkPump).FullName,
                        nameof(DeferredWorkPump.TryBegin));

                List<CodeInstruction> rewritten =
                    new List<CodeInstruction>(instructions);
                int executeIndex = rewritten.FindIndex(
                    instruction => instruction.Calls(execute));
                if (executeIndex < 0)
                {
                    throw new InvalidOperationException(
                        "Could not find RimWorld's deferred-work call site.");
                }

                Label continueOriginal = generator.DefineLabel();
                CodeInstruction original = rewritten[executeIndex];
                CodeInstruction loadCurrent =
                    new CodeInstruction(OpCodes.Ldsfld, currentEvent);
                loadCurrent.labels.AddRange(original.labels);
                original.labels.Clear();
                original.labels.Add(continueOriginal);
                rewritten.InsertRange(
                    executeIndex,
                    new[]
                    {
                        loadCurrent,
                        new CodeInstruction(OpCodes.Call, begin),
                        new CodeInstruction(
                            OpCodes.Brfalse,
                            continueOriginal),
                        new CodeInstruction(OpCodes.Ret)
                    });
                return rewritten;
            }
        }

        [HarmonyPatch]
        private static class ResolveDefinitionsPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                           typeof(PlayDataLoader),
                           "ResetStaticDataPre") ??
                       throw new MissingMethodException(
                           typeof(PlayDataLoader).FullName,
                           "ResetStaticDataPre");
            }

            [HarmonyPostfix]
            private static void Postfix()
            {
                RuntimeHost.TransitionStage(
                    PlayDataLoadStage.ReferenceResolution);
            }
        }

        [HarmonyPatch(
            typeof(DefGenerator),
            nameof(DefGenerator.GenerateImpliedDefs_PostResolve))]
        private static class PostResolveImpliedPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.TransitionStage(
                    PlayDataLoadStage.PostResolveImpliedDefinitions);
            }
        }

        [HarmonyPatch]
        private static class FinalizeDefinitionsPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                           typeof(PlayDataLoader),
                           "ResetStaticDataPost") ??
                       throw new MissingMethodException(
                           typeof(PlayDataLoader).FullName,
                           "ResetStaticDataPost");
            }

            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.TransitionStage(
                    PlayDataLoadStage.DefinitionFinalization);
            }
        }

        [HarmonyPatch(typeof(KeyPrefs), nameof(KeyPrefs.Init))]
        private static class InitializeRuntimePatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                RuntimeHost.TransitionStage(PlayDataLoadStage.InitializeRuntime);
            }
        }

        [HarmonyPatch]
        private static class DeferredWorkPatch
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
            private static void Prefix(out bool __state)
            {
                __state = RuntimeHost.TransitionStage(
                    PlayDataLoadStage.DeferredMainThreadWork);
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(
                Exception __exception,
                bool __state)
            {
                if (!__state)
                {
                    return __exception;
                }

                if (__exception == null)
                {
                    RuntimeHost.CompletePlayData();
                }
                else
                {
                    RuntimeHost.FailPlayData(__exception);
                }

                return __exception;
            }
        }
    }
}
