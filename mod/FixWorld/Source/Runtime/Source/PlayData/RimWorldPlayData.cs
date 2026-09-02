using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using FixWorld.Integration;
using HarmonyLib;
using RimWorld;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.PlayData
{
    internal sealed class RimWorldPlayData
    {
        private readonly MethodInfo resetStaticDataPre = RequireMethod(
            "ResetStaticDataPre");
        private readonly MethodInfo resetStaticDataPost = RequireMethod(
            "ResetStaticDataPost");

        internal void Reset()
        {
            GlobalTextureAtlasManager.ClearStaticAtlasBuildQueue();
            Profile("GraphicDatabase.Clear()", GraphicDatabase.Clear);
        }

        internal void ImportDefinitions()
        {
            Profile("Load language metadata.", LanguageDatabase.InitAllMetadata);
            LongEventHandler.SetCurrentEventText("LoadingDefs".Translate());
            Profile(
                "Copy all Defs from mods to global databases.",
                () => Parallel.ForEach(
                    typeof(Def).AllSubclasses(),
                    defType => GenGeneric.InvokeStaticMethodOnGenericType(
                        typeof(DefDatabase<>),
                        defType,
                        "AddAllInMods")));
        }

        internal void RunEarlyBinding()
        {
            Profile(
                "Resolve cross-references between non-implied Defs.",
                () => DirectXmlCrossRefLoader
                    .ResolveAllWantedCrossReferences(FailMode.Silent));
            Profile(
                "Rebind DefOfs (early).",
                () => DefOfHelper.RebindAllDefOfs(earlyTryMode: true));
            Profile("TKeySystem.BuildMappings()", TKeySystem.BuildMappings);
            Profile(
                "Legacy backstory translations.",
                () => BackstoryTranslationUtility.LoadAndInjectBackstoryData(
                    LanguageDatabase.activeLanguage.AllDirectories));
            Profile(
                "Inject selected language data into game data (early pass).",
                () => LanguageDatabase.activeLanguage
                    .InjectIntoData_BeforeImpliedDefs());
            Profile("Global operations (early pass).", ColoredText.ResetStaticData);
        }

        internal void GeneratePreResolveDefinitions()
        {
            Profile(
                "Generate implied Defs (pre-resolve).",
                () => DefGenerator.GenerateImpliedDefs_PreResolve());
        }

        internal void ResolveCrossReferences()
        {
            ProfileWithFinally(
                "Resolve cross-references between Defs made by the implied defs.",
                () => DirectXmlCrossRefLoader
                    .ResolveAllWantedCrossReferences(FailMode.LogErrors),
                DirectXmlCrossRefLoader.Clear);
            Profile(
                "Rebind DefOfs (final).",
                () => DefOfHelper.RebindAllDefOfs(earlyTryMode: false));
            Profile(
                "Other def binding, resetting and global operations (pre-resolve).",
                () => Invoke(resetStaticDataPre));
        }

        internal void ResolveDefinitions()
        {
            DeepProfiler.Start("Resolve references.");
            try
            {
                ResolveExactly<ThingCategoryDef>(parallel: true);
                ResolveExactly<RecipeDef>(parallel: true);
                Profile("Static resolver calls", () =>
                {
                    foreach (Type type in typeof(Def).AllSubclasses())
                    {
                        if (type != typeof(ThingDef) &&
                            type != typeof(ThingCategoryDef) &&
                            type != typeof(RecipeDef))
                        {
                            GenGeneric.InvokeStaticMethodOnGenericType(
                                typeof(DefDatabase<>),
                                type,
                                "ResolveAllReferences",
                                true,
                                false);
                        }
                    }
                });
                Profile(
                    "ThingDef resolver",
                    () => DefDatabase<ThingDef>.ResolveAllReferences());
            }
            finally
            {
                DeepProfiler.End();
            }
        }

        internal void GeneratePostResolveDefinitions()
        {
            Profile(
                "Generate implied Defs (post-resolve).",
                () => DefGenerator.GenerateImpliedDefs_PostResolve());
        }

        internal void FinalizeDefinitions()
        {
            Profile(
                "Other def binding, resetting and global operations (post-resolve).",
                () => Invoke(resetStaticDataPost));
            if (Prefs.DevMode)
            {
                Profile(
                    "Error check all defs.",
                    () => Parallel.ForEach(
                        typeof(Def).AllSubclasses(),
                        defType => GenGeneric.InvokeStaticMethodOnGenericType(
                            typeof(DefDatabase<>),
                            defType,
                            "ErrorCheckAllDefs")));
            }
        }

        internal void InitializeRuntime()
        {
            LongEventHandler.SetCurrentEventText("Initializing".Translate());
            Profile("Load keyboard preferences.", KeyPrefs.Init);
            Profile("Short hash giving.", ShortHashGiver.GiveAllShortHashes);
        }

        internal static void LoadBios()
        {
            Profile("Load all bios", SolidBioDatabase.LoadAllBios);
        }

        internal static void InjectLanguage()
        {
            Profile(
                "Inject selected language data into game data.",
                () =>
                {
                    LanguageDatabase.activeLanguage.InjectIntoData_AfterImpliedDefs();
                    GenLabel.ClearCache();
                });
        }

        internal static IReadOnlyList<Type> GetStaticConstructorTypes()
        {
            return GenTypes
                .AllTypesWithAttribute<StaticConstructorOnStartup>()
                .ToArray();
        }

        internal static bool TryGetSafeStaticConstructorPostfix(
            out MethodInfo postfix,
            out string postfixOwner,
            out string blockedOwners)
        {
            postfix = null;
            postfixOwner = null;
            blockedOwners = null;
            MethodInfo callAll = typeof(StaticConstructorOnStartupUtility)
                .GetMethod(
                    nameof(StaticConstructorOnStartupUtility.CallAll),
                    BindingFlags.Static | BindingFlags.Public);
            Patches patches = Harmony.GetPatchInfo(callAll);
            if (patches == null)
            {
                return true;
            }

            Patch[] prefixes = patches.Prefixes
                .Where(IsForeignPatch)
                .ToArray();
            Patch[] postfixes = patches.Postfixes
                .Where(IsForeignPatch)
                .ToArray();
            Patch[] transpilers = patches.Transpilers
                .Where(IsForeignPatch)
                .ToArray();
            Patch[] finalizers = patches.Finalizers
                .Where(IsForeignPatch)
                .ToArray();
            Patch[] all = prefixes
                .Concat(postfixes)
                .Concat(transpilers)
                .Concat(finalizers)
                .ToArray();
            if (all.Length == 0)
            {
                return true;
            }

            blockedOwners = string.Join(
                ",",
                all.Select(patch => patch.owner)
                    .Where(owner => !string.IsNullOrWhiteSpace(owner))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(owner => owner, StringComparer.Ordinal));
            if (prefixes.Length != 0 ||
                transpilers.Length != 0 ||
                finalizers.Length != 0 ||
                postfixes.Length != 1)
            {
                return false;
            }

            MethodInfo method = postfixes[0].PatchMethod;
            if (method == null ||
                !method.IsStatic ||
                method.ReturnType != typeof(void) ||
                method.GetParameters().Length != 0)
            {
                return false;
            }

            postfix = method;
            postfixOwner = postfixes[0].owner;
            blockedOwners = null;
            return true;
        }

        internal static void RunPatchedStaticConstructors()
        {
            StaticConstructorOnStartupUtility.CallAll();
            if (Prefs.DevMode)
            {
                StaticConstructorOnStartupUtility.ReportProbablyMissingAttributes();
            }
        }

        internal static void RunStaticConstructor(Type type)
        {
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }

        internal static void CompleteStaticConstructors()
        {
            StaticConstructorOnStartupUtility.coreStaticAssetsLoaded = true;
        }

        internal static void InvokeStaticConstructorPostfix(MethodInfo postfix)
        {
            Invoke(postfix);
        }

        internal static void ReportMissingStaticConstructorAttributes()
        {
            if (Prefs.DevMode)
            {
                StaticConstructorOnStartupUtility.ReportProbablyMissingAttributes();
            }
        }

        private static bool IsForeignPatch(Patch patch)
        {
            return patch != null &&
                   !RimWorldHooks.IsFixWorldOwner(patch.owner);
        }

        internal static void InitializeFloatMenus()
        {
            FloatMenuMakerMap.Init();
        }

        internal static void BakeStaticAtlases()
        {
            Profile("Atlas baking.", GlobalTextureAtlasManager.BakeStaticAtlases);
        }

        internal static void CollectUnusedAssets()
        {
            Profile("Garbage Collection", () =>
            {
                AbstractFilesystem.ClearAllCache();
                GC.Collect(int.MaxValue, GCCollectionMode.Forced);
                Resources.UnloadUnusedAssets();
            });
        }

        private static void ResolveExactly<TDef>(bool parallel)
            where TDef : Def
        {
            DeepProfiler.Start(typeof(TDef).Name + " resolver");
            try
            {
                DeepProfiler.enabled = false;
                DefDatabase<TDef>.ResolveAllReferences(
                    onlyExactlyMyType: true,
                    parallel: parallel);
            }
            finally
            {
                DeepProfiler.enabled = true;
                DeepProfiler.End();
            }
        }

        private static void Profile(string label, Action action)
        {
            DeepProfiler.Start(label);
            try
            {
                action();
            }
            finally
            {
                DeepProfiler.End();
            }
        }

        private static void ProfileWithFinally(
            string label,
            Action action,
            Action finallyAction)
        {
            DeepProfiler.Start(label);
            try
            {
                action();
            }
            finally
            {
                finallyAction();
                DeepProfiler.End();
            }
        }

        private static MethodInfo RequireMethod(string name)
        {
            return typeof(PlayDataLoader).GetMethod(
                       name,
                       BindingFlags.Static | BindingFlags.NonPublic) ??
                   throw new MissingMethodException(
                       typeof(PlayDataLoader).FullName,
                       name);
        }

        private static void Invoke(MethodInfo method)
        {
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }
    }
}
