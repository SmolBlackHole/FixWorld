using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FixWorld.Integration;
using HarmonyLib;
using RimWorld;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.Loading
{
    internal sealed class FinalizationPipeline
    {
        private static readonly MethodInfo CallAllMethod = AccessTools.Method(
            typeof(StaticConstructorOnStartupUtility),
            nameof(StaticConstructorOnStartupUtility.CallAll));
        private static readonly MethodInfo FloatMenuInitMethod = AccessTools.Method(
            typeof(FloatMenuMakerMap),
            nameof(FloatMenuMakerMap.Init));
        private static readonly MethodInfo BakeAtlasesMethod = AccessTools.Method(
            typeof(GlobalTextureAtlasManager),
            nameof(GlobalTextureAtlasManager.BakeStaticAtlases));
        private static readonly MethodInfo ClearFilesystemCacheMethod = AccessTools.Method(
            typeof(AbstractFilesystem),
            nameof(AbstractFilesystem.ClearAllCache));
        private static readonly MethodInfo CollectGarbageMethod = AccessTools.Method(
            typeof(GC),
            nameof(GC.Collect),
            new[] { typeof(int), typeof(GCCollectionMode) });
        private static readonly MethodInfo UnloadUnusedAssetsMethod = AccessTools.Method(
            typeof(Resources),
            nameof(Resources.UnloadUnusedAssets));

        private readonly IReadOnlyList<StaticConstructorTarget> constructors;
        private readonly string callAllPostfixOwners;

        private FinalizationPipeline()
        {
            constructors = GenTypes.AllTypesWithAttribute<StaticConstructorOnStartup>()
                .Select(StaticConstructorTarget.Create)
                .ToList();
            callAllPostfixOwners = GetHarmonyPostfixOwners(CallAllMethod);
        }

        internal static bool IsCandidate(MethodInfo method)
        {
            return method != null &&
                   method.Name == "<DoPlayLoad>b__4_4" &&
                   method.DeclaringType?.DeclaringType == typeof(PlayDataLoader);
        }

        internal static bool MatchesContract(MethodInfo method)
        {
            if (!IsCandidate(method) || !ContractMethodsAvailable())
            {
                return false;
            }

            try
            {
                List<CodeInstruction> instructions =
                    PatchProcessor.GetOriginalInstructions(method, null);
                return instructions.Any(item => item.Calls(CallAllMethod)) &&
                       instructions.Any(item => item.Calls(FloatMenuInitMethod)) &&
                       instructions.Any(item => item.Calls(BakeAtlasesMethod)) &&
                       instructions.Any(item => item.Calls(ClearFilesystemCacheMethod)) &&
                       instructions.Any(item => item.Calls(CollectGarbageMethod)) &&
                       instructions.Any(item => item.Calls(UnloadUnusedAssetsMethod));
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryCreateCompatible(
            Action action,
            out FinalizationPipeline pipeline)
        {
            pipeline = null;
            if (action == null || !ContractMethodsAvailable())
            {
                return false;
            }

            string unsupportedCallAllPatches = GetUnsupportedCallAllPatchOwners();
            string actionPatches = GetHarmonyPatchOwners(action.Method);
            if (unsupportedCallAllPatches != null || actionPatches != null)
            {
                Log.Message(
                    "[FixWorld] Static constructor staging skipped because Harmony " +
                    "patches are present; CallAll=" +
                    (unsupportedCallAllPatches ?? "none") +
                    ", finalization=" + (actionPatches ?? "none") + ".");
                return false;
            }

            string preservedPostfixes = GetHarmonyPostfixOwners(CallAllMethod);
            if (preservedPostfixes != null)
            {
                Log.Message(
                    "[FixWorld] Static constructor staging will preserve Harmony " +
                    "postfixes: " + preservedPostfixes + ".");
            }

            pipeline = new FinalizationPipeline();
            return true;
        }

        internal LoadingActionPlan CreatePlan(Action originalAction, string label)
        {
            List<LoadingPipelineStage> stages =
                new List<LoadingPipelineStage>(Prefs.DevMode ? 6 : 5);

            if (constructors.Count > 0)
            {
                LoadingWorkItem[] constructorTasks =
                    new LoadingWorkItem[constructors.Count];
                for (int index = 0; index < constructors.Count; index++)
                {
                    StaticConstructorTarget target = constructors[index];
                    string typeName = target.Type.FullName ?? target.Type.Name;
                    constructorTasks[index] = new LoadingWorkItem(
                        LoadingStage.Finalize,
                        LoadingStep.RunStaticConstructors,
                        "Initializing " + target.ModName,
                        typeName + "   " + (index + 1).ToString("N0") + " / " +
                        constructors.Count.ToString("N0"),
                        "StaticConstructorOnStartupUtility.CallAll()",
                        typeName,
                        LoadingModAttribution.Exact(target.PackageId, target.ModName),
                        continueOnFailure: true,
                        execute: () => RunConstructor(target));
                }

                stages.Add(new LoadingPipelineStage(
                    "Static constructors",
                    LoadingStage.Finalize,
                    LoadingStep.RunStaticConstructors,
                    LoadingExecutionMode.MainThread,
                    constructorTasks));
            }

            AddMainThreadStage(
                stages,
                new LoadingWorkItem(
                    LoadingStage.Finalize,
                    LoadingStep.FinalizeStaticInitialization,
                    "Finalizing mod frameworks",
                    callAllPostfixOwners == null
                        ? "Completing RimWorld static initialization"
                        : "Harmony postfixes: " + callAllPostfixOwners,
                    "Finalize static initialization",
                    "Static initialization",
                    LoadingModAttribution.Global,
                    continueOnFailure: false,
                    execute: CompleteStaticInitialization));

            if (Prefs.DevMode)
            {
                AddMainThreadStage(
                    stages,
                    new LoadingWorkItem(
                        LoadingStage.Finalize,
                        LoadingStep.CheckStaticConstructorAttributes,
                        "Checking startup attributes",
                        "Developer-mode validation",
                        "Check static constructor attributes",
                        "Static constructor attributes",
                        LoadingModAttribution.Global,
                        continueOnFailure: false,
                        execute: CheckMissingAttributes));
            }

            AddMainThreadStage(
                stages,
                new LoadingWorkItem(
                    LoadingStage.Finalize,
                    LoadingStep.InitializeFloatMenus,
                    "Initializing runtime",
                    "Building float-menu data",
                    null,
                    "Float menus",
                    LoadingModAttribution.Global,
                    continueOnFailure: false,
                    execute: InitializeFloatMenus));
            AddMainThreadStage(
                stages,
                new LoadingWorkItem(
                    LoadingStage.Finalize,
                    LoadingStep.BakeAtlases,
                    "Building texture atlases",
                    "Atlas baking",
                    "Atlas baking.",
                    "Texture atlases",
                    LoadingModAttribution.Global,
                    continueOnFailure: false,
                    execute: BakeAtlases));
            AddMainThreadStage(
                stages,
                new LoadingWorkItem(
                    LoadingStage.Finalize,
                    LoadingStep.GarbageCollection,
                    "Cleaning up loading data",
                    "Garbage collection and unused asset cleanup",
                    "Garbage Collection",
                    "Loading cleanup",
                    LoadingModAttribution.Global,
                    continueOnFailure: false,
                    execute: CleanUp));

            return new LoadingActionPlan(
                label,
                LoadingAttributionResolver.Infer(originalAction),
                stages);
        }

        private static void AddMainThreadStage(
            ICollection<LoadingPipelineStage> stages,
            LoadingWorkItem task)
        {
            stages.Add(new LoadingPipelineStage(
                task.DisplayName,
                task.Stage,
                task.Operation,
                LoadingExecutionMode.MainThread,
                task));
        }

        private static void RunConstructor(StaticConstructorTarget target)
        {
            RuntimeHelpers.RunClassConstructor(target.Type.TypeHandle);
        }

        private static void CompleteStaticInitialization()
        {
            StaticConstructorOnStartupUtility.CallAll();
        }

        private static void CheckMissingAttributes()
        {
            StaticConstructorOnStartupUtility.ReportProbablyMissingAttributes();
        }

        private static void InitializeFloatMenus()
        {
            FloatMenuMakerMap.Init();
        }

        private static void BakeAtlases()
        {
            GlobalTextureAtlasManager.BakeStaticAtlases();
        }

        private static void CleanUp()
        {
            AbstractFilesystem.ClearAllCache();
            GC.Collect(int.MaxValue, GCCollectionMode.Forced);
            Resources.UnloadUnusedAssets();
        }

        private static bool ContractMethodsAvailable()
        {
            return CallAllMethod != null &&
                   FloatMenuInitMethod != null &&
                   BakeAtlasesMethod != null &&
                   ClearFilesystemCacheMethod != null &&
                   CollectGarbageMethod != null &&
                   UnloadUnusedAssetsMethod != null;
        }

        private static string GetHarmonyPatchOwners(MethodBase method)
        {
            return HarmonyPatchInspector.GetOwners(method);
        }

        private static string GetUnsupportedCallAllPatchOwners()
        {
            return HarmonyPatchInspector.GetOwners(
                CallAllMethod,
                HarmonyPatchKinds.Prefix |
                HarmonyPatchKinds.Transpiler |
                HarmonyPatchKinds.Finalizer);
        }

        private static string GetHarmonyPostfixOwners(MethodBase method)
        {
            return HarmonyPatchInspector.GetOwners(
                method,
                HarmonyPatchKinds.Postfix);
        }
    }

    internal readonly struct StaticConstructorTarget
    {
        internal readonly Type Type;
        internal readonly string PackageId;
        internal readonly string ModName;

        private StaticConstructorTarget(Type type, string packageId, string modName)
        {
            Type = type;
            PackageId = packageId;
            ModName = modName;
        }

        internal static StaticConstructorTarget Create(Type type)
        {
            ModContentPack mod = LoadedModManager.RunningModsListForReading
                .FirstOrDefault(item => item.assemblies.loadedAssemblies.Contains(type.Assembly));
            if (mod != null)
            {
                return new StaticConstructorTarget(type, mod.PackageId, mod.Name);
            }

            string assemblyName = type.Assembly.GetName().Name ?? "unknown";
            if (type.Assembly == typeof(StaticConstructorOnStartupUtility).Assembly)
            {
                return new StaticConstructorTarget(
                    type,
                    ModContentPack.CoreModPackageId,
                    "RimWorld");
            }

            return new StaticConstructorTarget(type, assemblyName, assemblyName);
        }
    }
}
