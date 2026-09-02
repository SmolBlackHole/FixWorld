extern alias FixWorldShared;

using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using SharedProfiling = FixWorldShared::FixWorld.Profiling;

namespace FixWorld.PlayData
{
    internal static class PlayDataLoadPipeline
    {
        private static readonly MethodInfo ResetStaticDataPreMethod =
            FindPlayDataLoaderMethod("ResetStaticDataPre");
        private static readonly MethodInfo ResetStaticDataPostMethod =
            FindPlayDataLoaderMethod("ResetStaticDataPost");
        private static readonly Action LoadAllBiosAction =
            FindDeferredAction("<DoPlayLoad>b__4_2");
        private static readonly Action InjectLanguageAction =
            FindDeferredAction("<DoPlayLoad>b__4_3");
        private static readonly Action FinalizePlayDataAction =
            FindDeferredAction("<DoPlayLoad>b__4_4");
        private static readonly ProfilingPlayDataLoadObserver Observer =
            new ProfilingPlayDataLoadObserver();

        internal static void Run()
        {
            PlayDataLoadContext context = new PlayDataLoadContext(Observer);

            context.Run(PlayDataLoadStage.Reset, () =>
            {
                GlobalTextureAtlasManager.ClearStaticAtlasBuildQueue();
                Profile("GraphicDatabase.Clear()", GraphicDatabase.Clear);
            });

            context.Run(
                PlayDataLoadStage.ModBoot,
                () => Profile(
                    "Load all active mods.",
                    () => LoadedModManager.LoadAllActiveMods()));

            context.Run(PlayDataLoadStage.LanguageMetadata, () =>
            {
                Profile(
                    "Load language metadata.",
                    LanguageDatabase.InitAllMetadata);
                LongEventHandler.SetCurrentEventText("LoadingDefs".Translate());
            });

            context.Run(PlayDataLoadStage.DefinitionImport, () =>
                Profile(
                    "Copy all Defs from mods to global databases.",
                    () => Parallel.ForEach(
                        typeof(Def).AllSubclasses(),
                        defType => GenGeneric.InvokeStaticMethodOnGenericType(
                            typeof(DefDatabase<>),
                            defType,
                            "AddAllInMods"))));

            context.Run(PlayDataLoadStage.EarlyBinding, () =>
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
                Profile(
                    "Global operations (early pass).",
                    ColoredText.ResetStaticData);
            });

            context.Run(PlayDataLoadStage.PreResolveImpliedDefinitions, () =>
                Profile(
                    "Generate implied Defs (pre-resolve).",
                    () => DefGenerator.GenerateImpliedDefs_PreResolve()));

            context.Run(PlayDataLoadStage.CrossReferenceResolution, () =>
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
                    () => InvokePlayDataLoaderMethod(ResetStaticDataPreMethod));
            });

            context.Run(
                PlayDataLoadStage.ReferenceResolution,
                ResolveReferences);

            context.Run(PlayDataLoadStage.PostResolveImpliedDefinitions, () =>
                Profile(
                    "Generate implied Defs (post-resolve).",
                    () => DefGenerator.GenerateImpliedDefs_PostResolve()));

            context.Run(PlayDataLoadStage.DefinitionFinalization, () =>
            {
                Profile(
                    "Other def binding, resetting and global operations (post-resolve).",
                    () => InvokePlayDataLoaderMethod(ResetStaticDataPostMethod));
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
            });

            context.Run(PlayDataLoadStage.Initialization, () =>
            {
                LongEventHandler.SetCurrentEventText("Initializing".Translate());
                Profile("Load keyboard preferences.", KeyPrefs.Init);
                Profile("Short hash giving.", ShortHashGiver.GiveAllShortHashes);
            });

            context.Run(
                PlayDataLoadStage.DeferredInitialization,
                EnqueueDeferredInitialization);
        }

        internal static SharedProfiling.ProfileSnapshot<PlayDataLoadStage>
            CaptureProfile()
        {
            return Observer.Snapshot();
        }

        private static void ResolveReferences()
        {
            DeepProfiler.Start("Resolve references.");
            try
            {
                DeepProfiler.Start("ThingCategoryDef resolver");
                try
                {
                    DeepProfiler.enabled = false;
                    DefDatabase<ThingCategoryDef>.ResolveAllReferences(
                        onlyExactlyMyType: true,
                        parallel: true);
                    DeepProfiler.enabled = true;
                }
                finally
                {
                    DeepProfiler.End();
                }

                DeepProfiler.Start("RecipeDef resolver");
                try
                {
                    DeepProfiler.enabled = false;
                    DefDatabase<RecipeDef>.ResolveAllReferences(
                        onlyExactlyMyType: true,
                        parallel: true);
                    DeepProfiler.enabled = true;
                }
                finally
                {
                    DeepProfiler.End();
                }

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

        private static void EnqueueDeferredInitialization()
        {
            LongEventHandler.ExecuteWhenFinished(LoadAllBiosAction);
            LongEventHandler.ExecuteWhenFinished(InjectLanguageAction);
            LongEventHandler.ExecuteWhenFinished(FinalizePlayDataAction);
            LongEventHandler.ExecuteWhenFinished(Log.ResetMessageCount);
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

        private static MethodInfo FindPlayDataLoaderMethod(string name)
        {
            return typeof(PlayDataLoader).GetMethod(
                       name,
                       BindingFlags.Static | BindingFlags.NonPublic) ??
                   throw new MissingMethodException(
                       typeof(PlayDataLoader).FullName,
                       name);
        }

        private static Action FindDeferredAction(string name)
        {
            const BindingFlags Flags =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.Instance;
            Type closureType = typeof(PlayDataLoader).GetNestedType("<>c", Flags) ??
                               throw new MissingMemberException(
                                   typeof(PlayDataLoader).FullName,
                                   "<>c");
            object closure = closureType.GetField("<>9", Flags)?.GetValue(null) ??
                             throw new MissingFieldException(
                                 closureType.FullName,
                                 "<>9");
            MethodInfo method = closureType.GetMethod(name, Flags) ??
                                throw new MissingMethodException(
                                    closureType.FullName,
                                    name);
            return (Action)Delegate.CreateDelegate(
                typeof(Action),
                closure,
                method,
                throwOnBindFailure: true);
        }

        private static void InvokePlayDataLoaderMethod(MethodInfo method)
        {
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
