using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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

        internal IReadOnlyList<StaticConstructorTarget> Constructors { get; }
        internal string CallAllPostfixOwners { get; }
        internal bool ShouldCheckMissingAttributes => Prefs.DevMode;

        private FinalizationPipeline()
        {
            Constructors = GenTypes.AllTypesWithAttribute<StaticConstructorOnStartup>()
                .Select(StaticConstructorTarget.Create)
                .ToList();
            CallAllPostfixOwners = GetHarmonyPostfixOwners(CallAllMethod);
        }

        internal static bool TryCreate(Action action, out FinalizationPipeline pipeline)
        {
            pipeline = null;
            if (action == null ||
                !ContractMethodsAvailable() ||
                !CallsExpectedFinalizationMethods(action.Method))
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

        internal static void RunConstructor(StaticConstructorTarget target)
        {
            RuntimeHelpers.RunClassConstructor(target.Type.TypeHandle);
        }

        internal static void CompleteStaticInitialization()
        {
            StaticConstructorOnStartupUtility.CallAll();
        }

        internal static void CheckMissingAttributes()
        {
            StaticConstructorOnStartupUtility.ReportProbablyMissingAttributes();
        }

        internal static void InitializeFloatMenus()
        {
            FloatMenuMakerMap.Init();
        }

        internal static void BakeAtlases()
        {
            GlobalTextureAtlasManager.BakeStaticAtlases();
        }

        internal static void CleanUp()
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

        private static bool CallsExpectedFinalizationMethods(MethodInfo method)
        {
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

        private static string GetHarmonyPatchOwners(MethodBase method)
        {
            Patches patches = Harmony.GetPatchInfo(method);
            return patches == null
                ? null
                : JoinOwners(
                    patches.Prefixes
                        .Concat(patches.Postfixes)
                        .Concat(patches.Transpilers)
                        .Concat(patches.Finalizers));
        }

        private static string GetUnsupportedCallAllPatchOwners()
        {
            Patches patches = Harmony.GetPatchInfo(CallAllMethod);
            return patches == null
                ? null
                : JoinOwners(
                    patches.Prefixes
                        .Concat(patches.Transpilers)
                        .Concat(patches.Finalizers));
        }

        private static string GetHarmonyPostfixOwners(MethodBase method)
        {
            Patches patches = Harmony.GetPatchInfo(method);
            return patches == null ? null : JoinOwners(patches.Postfixes);
        }

        private static string JoinOwners(IEnumerable<Patch> patches)
        {
            string[] owners = patches
                .Select(item => item.owner)
                .Where(owner => !string.IsNullOrWhiteSpace(owner))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(owner => owner, StringComparer.Ordinal)
                .ToArray();
            return owners.Length == 0 ? null : string.Join(",", owners);
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
