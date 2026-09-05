// SPDX-License-Identifier: MPL-2.0
using System;
using System.Diagnostics;
using FixWorld.Telemetry;
using FixWorld.Caching;
using FixWorld.Bootstrap;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using FixWorld.Core;
using FixWorld.Logs;
using FixWorld.News;
using FixWorld.Quickstart;
using FixWorld.Settings;
using FixWorld.Spotter;
using FixWorld.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Verse;

[assembly: InternalsVisibleTo("FixWorldTests")]

namespace FixWorld
{
    /// <summary>
    /// The hub of the library. Instantiates classes that extend ModBase and forwards some of the more useful events to them.
    /// The assembly version of the library should reflect the current major Rimworld version, i.e.: 0.18.0.0 for B18.
    /// This gives us the ability to release updates to the library without breaking compatibility with the mods that implement it.
    /// See Core.FixWorldMod for the entry point.
    /// </summary>
    public class FixWorldController
    {
        private const string SceneObjectName = "FixWorldProxy";
        private const string ModIdentifier = "FixWorld";
        private const string ModPackName = "FixWorld";
        private const string HarmonyInstanceIdentifier = "smolblackhole.fixworld";
        private const string HarmonyDebugCommandLineArg = "harmony_debug";

        private static readonly object InstanceSync = new();
        private static FixWorldController instance;

        public static FixWorldController Instance
        {
            get { lock (InstanceSync) return instance ?? (instance = new FixWorldController()); }
        }

        private static VersionFile libraryVersionFile;
        private static AssemblyVersionInfo libraryVersionInfo;
        public static Version LibraryVersion
        {
            get
            {
                if (libraryVersionInfo == null) ReadOwnVersion();
                if (libraryVersionFile != null && libraryVersionFile.OverrideVersion != null)
                    return libraryVersionFile.OverrideVersion;
                if (libraryVersionInfo != null) return libraryVersionInfo.HighestVersion;
                return typeof(FixWorldController).Assembly.GetName().Version;
            }
        }

        public static ModSettingsManager SettingsManager
        {
            get { return Instance.Settings; }
        }

        internal static ModContentPack OwnContentPack { get; private set; }
        internal static ModSettingsPack OwnSettingsPack { get; private set; }

        // most of the initialization happens during Verse.Mod instantiation. Pretty much no vanilla data is yet loaded at this point.
        internal static void EarlyInitialize(ModContentPack contentPack)
        {
            try
            {
                BootSession.Current.Attach(typeof(FixWorldController).Assembly, contentPack, () =>
                {
                    OwnContentPack = contentPack;
                    Instance.InitializeController();
                    CreateSceneObject();
                });
                BootstrapIntegration.ConfirmAttachment();
            }
            catch (Exception e)
            {
                BootSession.Current.Fail(e);
                Instance.DisposeCore();
                Logger.Error("An exception occurred during early initialization: " + e);
            }
        }

        // Called by the in-process preloader, before ModContentPack attachment.
        // No Verse/Unity state, scene objects or mod callbacks may be used here.
        public static void StartEarly()
        {
            BootSession.Current.StartCore(typeof(FixWorldController).Assembly, () =>
            {
                var controller = Instance;
                try
                {
                    controller.Diagnostics = new LibraryDiagnostics();
                    controller.Caches = new CacheStore(controller.Diagnostics.Store, controller.Diagnostics.Profiler);
                    BootstrapIntegration.RegisterTelemetry(controller.Diagnostics);
                    controller.StartCapture();
                }
                catch { controller.DisposeCore(); throw; }
            });
            BootstrapIntegration.Publish();
        }

        private TelemetryCapture telemetryCapture;
        private void StartCapture()
        {
            if (telemetryCapture != null || Diagnostics == null) return;
            try
            {
                var directory = System.IO.Path.Combine(
                    BootEnvironment.SaveDataFolder(Environment.GetCommandLineArgs()), "FixWorld", "Telemetry");
                telemetryCapture = new TelemetryCapture(Diagnostics.Store, directory, BootEnvironment.Log);
            }
            catch (Exception error) { BootEnvironment.Log("Telemetry capture unavailable: " + error); }
        }

        private static ModLogger _logger;
        internal static ModLogger Logger
        {
            get
            {
                return _logger ?? (_logger = new ModLogger(ModIdentifier));
            }
        }

        private static void CreateSceneObject()
        {
            // this must execute in the main thread
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (!BootSession.Current.IsAttached) return;
                if (GameObject.Find(SceneObjectName) != null)
                {
                    Logger.Error("Another version of the library is already loaded. The FixWorld assembly should be loaded as a standalone mod.");
                    return;
                }
                var obj = new GameObject(SceneObjectName);
                UnityEngine.Object.DontDestroyOnLoad(obj);
                obj.AddComponent<UnityProxyComponent>();
            });
        }

        private static void ReadOwnVersion()
        {
            var ownAssembly = typeof(FixWorldController).Assembly;
            if (OwnContentPack != null)
            {
                libraryVersionFile = VersionFile.TryParseVersionFile(OwnContentPack);
                libraryVersionInfo = AssemblyVersionInfo.ReadModAssembly(ownAssembly, OwnContentPack);
            }
            else
            {
                Logger.Error("Failed to identify own ModContentPack");
            }
        }

        private readonly List<ModBase> childMods = new List<ModBase>();
        private readonly List<ModBase> earlyInitializedMods = new List<ModBase>();
        private readonly List<ModBase> initializedMods = new List<ModBase>();
        private readonly HashSet<Assembly> autoHarmonyPatchedAssemblies = new HashSet<Assembly>();
        private Dictionary<Assembly, ModContentPack> assemblyContentPacks;
        private bool initializationInProgress;
        private bool shuttingDown;
        private readonly Func<LibraryState> captureLibraryState;

        public LibraryDiagnostics Diagnostics { get; private set; }
        public CacheStore Caches { get; private set; }
        internal TextMeasurementCache TextMeasurements { get; private set; }

        public ModSettingsManager Settings { get; private set; }
        public UpdateFeatureManager UpdateFeatures { get; private set; }
        public TickDelayScheduler TickDelayScheduler { get; private set; }
        public DistributedTickScheduler DistributedTicker { get; private set; }
        public DoLaterScheduler DoLater { get; private set; }
        public LogPublisher LogUploader { get; private set; }
        public ModSpottingManager ModSpotter { get; private set; }

        internal Harmony HarmonyInst { get; private set; }
        internal IEnumerable<ModBase> InitializedMods
        {
            get { return initializedMods; }
        }

        private FixWorldController()
        {
            captureLibraryState = () => new LibraryState(initializedMods.Count,
                TickDelayScheduler?.GetAllPendingCallbacks().Count() ?? 0,
                DistributedTicker?.CaptureTelemetry() ?? default);
        }

        // called during Verse.Mod instantiation
        private void InitializeController()
        {
            try
            {
                TextMeasurements = new TextMeasurementCache(Caches);
                ReadOwnVersion();
                Logger.Message("version {0}", LibraryVersion);
                PrepareReflection();
                ApplyHarmonyPatches();
                Settings = new ModSettingsManager();
                Settings.BeforeModSettingsSaved += OnBeforeModSettingsSaved;
                UpdateFeatures = new UpdateFeatureManager();
                UpdateFeatures.OnEarlyInitialize();
                TickDelayScheduler = new TickDelayScheduler();
                DistributedTicker = new DistributedTickScheduler();
                DoLater = new DoLaterScheduler();
                LogUploader = new LogPublisher();
                var librarySettings = Settings.GetModSettings(ModIdentifier);
                QuickstartController.OnEarlyInitialize(librarySettings);
                ModSpotter = new ModSpottingManager();
                ModSpotter.OnEarlyInitialize();
                new LibraryVersionChecker(LibraryVersion, Logger).OnEarlyInitialize();
                LoadOrderChecker.ValidateLoadOrder();
                EnumerateModAssemblies();
                EarlyInitializeChildMods();
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
                throw;
            }
        }

        private void EarlyInitializeChildMods()
        {
            try
            {
                initializationInProgress = true;
                EnumerateChildMods(true);
                for (int i = 0; i < childMods.Count; i++)
                {
                    var childMod = childMods[i];
                    if (earlyInitializedMods.Contains(childMod)) continue;
                    earlyInitializedMods.Add(childMod);
                    var modId = childMod.LogIdentifierSafe;
                    try
                    {
                        childMod.EarlyInitialize();
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, modId);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
            finally
            {
                initializationInProgress = false;
            }
        }

        // called during static constructor initialization
        internal void LateInitialize()
        {
            if (!BootSession.Current.IsAttached) return;
            try
            {
                BootSession.Current.BeginCompletion(() =>
                {
                    RegisterOwnSettings();
                    QuickstartController.OnLateInitialize();
                    LongEventHandler.ExecuteWhenFinished(HarmonyUtility.LogHarmonyPatchIssueErrors);
                    LongEventHandler.QueueLongEvent(() =>
                    {
                        try
                        {
                            BootSession.Current.Complete(LoadReloadInitialize);
                            BootstrapIntegration.Publish();
                        }
                        catch (Exception error) { DisposeCore(); Logger.ReportException(error); }
                    }, "Initializing", true, null);
                });
                BootstrapIntegration.Publish();
            }
            catch (Exception e)
            {
                DisposeCore();
                Logger.Error("An exception occurred during late initialization: " + e);
            }
        }

        // executed both at startup and after a def reload
        internal void LoadReloadInitialize()
        {
            try
            {
                initializationInProgress = true; // prevent the Unity events from causing race conditions during async loading
                CheckForIncludedFixWorldAssembly();
                EnumerateModAssemblies();
                EnumerateChildMods(false);
                for (int i = 0; i < childMods.Count; i++)
                {
                    var childMod = childMods[i];
                    childMod.ModIsActive = assemblyContentPacks.ContainsKey(childMod.GetType().Assembly);
                    if (initializedMods.Contains(childMod)) continue; // no need to reinitialize already loaded mods
                    initializedMods.Add(childMod);
                    var modId = childMod.LogIdentifierSafe;
                    try
                    {
                        childMod.StaticInitialize();
                        childMod.Initialize();
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, modId);
                    }
                }
                OnDefsLoaded();
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
                if (BootSession.Current.Phase == BootPhase.Completing) throw;
            }
            finally
            {
                initializationInProgress = false;
            }
        }

        internal void OnUpdate()
        {
            if (BootSession.Current.Phase != BootPhase.Ready || initializationInProgress || shuttingDown || Diagnostics == null) return;
            Caches.BindCurrentThread();
            Diagnostics.RecordFrame();
            using var measurement = Diagnostics.Update.Measure();
            try
            {
                DoLater?.OnUpdate();
                for (int i = 0; i < initializedMods.Count; i++)
                {
                    try
                    {
                        initializedMods[i].Update();
                    }
                    catch (Exception e)
                    {
                        Diagnostics.RecordCallbackError();
                        Logger.ReportException(e, initializedMods[i].LogIdentifierSafe, true);
                    }
                }
            }
            catch (Exception e)
            {
                measurement.Fail();
                Logger.ReportException(e, null, true);
            }
            finally
            {
                measurement.Complete();
                try
                {
                    if (Diagnostics.PublishIfDue(Stopwatch.GetTimestamp(), captureLibraryState)) Caches.Publish();
                }
                catch (Exception error) { Logger.ReportException(error, "telemetry publication", true); }
            }
        }

        internal void OnTick()
        {
            if (BootSession.Current.Phase != BootPhase.Ready || initializationInProgress || shuttingDown || Diagnostics == null) return;
            Diagnostics.RecordTick();
            using var measurement = Diagnostics.Tick.Measure();
            try
            {
                DoLater.OnTick();
                var currentTick = Find.TickManager.TicksGame;
                for (int i = 0; i < initializedMods.Count; i++)
                {
                    try
                    {
                        initializedMods[i].Tick(currentTick);
                    }
                    catch (Exception e)
                    {
                        Diagnostics.RecordCallbackError();
                        Logger.ReportException(e, initializedMods[i].LogIdentifierSafe, true);
                    }
                }
                TickDelayScheduler.Tick(currentTick);
                DistributedTicker.Tick(currentTick);
            }
            catch (Exception e)
            {
                measurement.Fail();
                Logger.ReportException(e, null, true);
            }
        }

        internal void OnFixedUpdate()
        {
            if (BootSession.Current.Phase != BootPhase.Ready || initializationInProgress || shuttingDown || Diagnostics == null) return;
            using var measurement = Diagnostics.FixedUpdate.Measure();
            try
            {
                for (int i = 0; i < initializedMods.Count; i++)
                {
                    try
                    {
                        initializedMods[i].FixedUpdate();
                    }
                    catch (Exception e)
                    {
                        Diagnostics.RecordCallbackError();
                        Logger.ReportException(e, initializedMods[i].LogIdentifierSafe, true);
                    }
                }
            }
            catch (Exception e)
            {
                measurement.Fail();
                Logger.ReportException(e, null, true);
            }
        }

        internal void OnGUI()
        {
            if (BootSession.Current.Phase != BootPhase.Ready || initializationInProgress || shuttingDown || Diagnostics == null) return;
            using var measurement = Diagnostics.OnGUI.Measure();
            try
            {
                DoLater?.OnGUI();
                KeyBindingHandler.OnGUI();
                for (int i = 0; i < initializedMods.Count; i++)
                {
                    try
                    {
                        initializedMods[i].OnGUI();
                    }
                    catch (Exception e)
                    {
                        Diagnostics.RecordCallbackError();
                        Logger.ReportException(e, initializedMods[i].LogIdentifierSafe, true);
                    }
                }
            }
            catch (Exception e)
            {
                measurement.Fail();
                Logger.ReportException(e, null, true);
            }
        }

        internal void OnGUIUnfiltered()
        {
            QuickstartController.OnGUIUnfiltered();
        }

        internal void OnSceneLoaded(Scene scene)
        {
            try
            {
                for (int i = 0; i < childMods.Count; i++)
                {
                    try
                    {
                        childMods[i].SceneLoaded(scene);
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, childMods[i].LogIdentifierSafe);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }

        internal void OnApplicationQuit()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            try
            {
                for (int i = 0; i < childMods.Count; i++)
                {
                    try
                    {
                        childMods[i].ApplicationQuit();
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, childMods[i].LogIdentifierSafe);
                    }
                }
                Settings.SaveChanges();
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
            finally
            {
                BootSession.Current.Stop();
                DisposeCore();
            }
        }

        private void DisposeCore()
        {
            telemetryCapture?.Dispose();
            // Failed attachment precedes cache thread binding. Normal quit is on
            // its bound main thread. The exporter has already been signaled above.
            if (BootSession.Current.Phase == BootPhase.Failed) HarmonyInst?.UnpatchAll(HarmonyInstanceIdentifier);
            try { Caches?.Dispose(); }
            finally { Diagnostics?.Dispose(); }
        }

        internal void OnGameInitializationStart(Game game)
        {
            try
            {
                var currentTick = game.tickManager.TicksGame;
                TickDelayScheduler.Initialize(currentTick);
                DistributedTicker.Initialize(currentTick);
                game.tickManager.RegisterAllTickabilityFor(new FixWorldTickProxy { CreatedByController = true });
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }

        internal void OnPlayingStateEntered()
        {
            try
            {
                UtilityWorldObjectManager.OnWorldLoaded();
                for (int i = 0; i < childMods.Count; i++)
                {
                    try
                    {
                        childMods[i].WorldLoaded();
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, childMods[i].LogIdentifierSafe);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }

        internal void OnMapGenerated(Map map)
        {
            try
            {
                for (int i = 0; i < childMods.Count; i++)
                {
                    try
                    {
                        childMods[i].MapGenerated(map);
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, childMods[i].LogIdentifierSafe);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }

        internal void OnMapComponentsConstructed(Map map)
        {
            try
            {
                for (int i = 0; i < childMods.Count; i++)
                {
                    try
                    {
                        childMods[i].MapComponentsInitializing(map);
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, childMods[i].LogIdentifierSafe);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }

        internal void OnMapInitFinalized(Map map)
        {
            // Make sure we execute OnMapLoaded after MapDrawer.RegenerateEverythingNow
            LongEventHandler.QueueLongEvent(() => OnMapLoaded(map), null, false, null);
        }

        internal bool ShouldHarmonyAutoPatch(Assembly assembly, string modId)
        {
            if (autoHarmonyPatchedAssemblies.Contains(assembly))
            {
                Logger.Warning("The {0} assembly contains multiple ModBase mods with HarmonyAutoPatch set to true. This warning was caused by modId {1}.", assembly.GetName().Name, modId);
                return false;
            }
            else
            {
                autoHarmonyPatchedAssemblies.Add(assembly);
                return true;
            }
        }

        private void OnMapLoaded(Map map)
        {
            try
            {
                DoLater.OnMapLoaded(map);
                for (int i = 0; i < childMods.Count; i++)
                {
                    try
                    {
                        childMods[i].MapLoaded(map);
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, childMods[i].LogIdentifierSafe);
                    }
                }
                // show update news dialog
                UpdateFeatures.TryShowDialog(false);
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }

        internal void OnMapDiscarded(Map map)
        {
            try
            {
                for (int i = 0; i < childMods.Count; i++)
                {
                    try
                    {
                        childMods[i].MapDiscarded(map);
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, childMods[i].LogIdentifierSafe);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }

        private void OnBeforeModSettingsSaved()
        {
            try
            {
                for (int i = 0; i < initializedMods.Count; i++)
                {
                    try
                    {
                        var mod = initializedMods[i];
                        if (mod.SettingsPackInternalAccess != null && mod.SettingsPackInternalAccess.HasUnsavedChanges)
                        {
                            initializedMods[i].SettingsChanged();
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, initializedMods[i].LogIdentifierSafe);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }

        private void OnDefsLoaded()
        {
            Caches?.InvalidateAll();
            try
            {
                UtilityWorldObjectManager.OnDefsLoaded();
                for (int i = 0; i < childMods.Count; i++)
                {
                    try
                    {
                        childMods[i].DefsLoaded();
                    }
                    catch (Exception e)
                    {
                        Logger.ReportException(e, childMods[i].LogIdentifierSafe);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }

        // will run on startup and on reload. On reload it will add newly loaded mods
        private void EnumerateChildMods(bool earlyInitMode)
        {
            var modBaseDescendantsInLoadOrder = typeof(ModBase).InstantiableDescendantsAndSelf()
                .Select(t => new Pair<Type, ModContentPack>(t, assemblyContentPacks.TryGetValue(t.Assembly)))
                .Where(pair => pair.Second != null) // null pack => mod is disabled
                .OrderBy(pair => pair.Second.loadOrder).ToArray();

            var instantiatedThisRun = new List<string>();
            foreach (var pair in modBaseDescendantsInLoadOrder)
            {
                var subclass = pair.First;
                var pack = pair.Second;
                var hasEarlyInit = subclass.HasAttribute<EarlyInitAttribute>();
                if (hasEarlyInit != earlyInitMode) continue;
                if (childMods.Find(cm => cm.GetType() == subclass) != null) continue; // skip duplicate types present in multiple assemblies
                try
                {
                    ModBase.CurrentlyProcessedContentPack = pack;
                    var modbase = (ModBase)Activator.CreateInstance(subclass, true);
                    ModBase.CurrentlyProcessedContentPack = null;
                    modbase.ApplyHarmonyPatches();
                    modbase.VersionInfo = AssemblyVersionInfo.ReadModAssembly(subclass.Assembly, pack);
                    childMods.Add(modbase);
                    instantiatedThisRun.Add(modbase.LogIdentifierSafe);
                }
                catch (Exception e)
                {
                    Logger.ReportException(e, subclass.ToString(), false, "child mod instantiation");
                }
            }
            if (instantiatedThisRun.Count > 0)
            {
                var template = earlyInitMode ? "early-initializing {0}" : "initializing {0}";
                Logger.Message(template, instantiatedThisRun.ListElements());
            }
        }

        private void EnumerateModAssemblies()
        {
            assemblyContentPacks = new Dictionary<Assembly, ModContentPack>();
            foreach (var modContentPack in LoadedModManager.RunningMods)
            {
                foreach (var loadedAssembly in modContentPack.assemblies.loadedAssemblies)
                {
                    assemblyContentPacks[loadedAssembly] = modContentPack;
                }
            }
        }

        // Ensure that no other mod has accidentally included the dll
        private void CheckForIncludedFixWorldAssembly()
        {
            var controllerTypeName = GetType().FullName;
            if (controllerTypeName == null) throw new NullReferenceException();
            foreach (var modContentPack in LoadedModManager.RunningMods)
            {
                foreach (var loadedAssembly in modContentPack.assemblies.loadedAssemblies)
                {
                    if (loadedAssembly.GetType(controllerTypeName, false) != null && modContentPack.Name != ModPackName)
                    {
                        Logger.Error("Found FixWorld assembly included by mod {0}. The dll should never be included by other mods.", modContentPack.Name);
                    }
                }
            }
        }

        private void ApplyHarmonyPatches()
        {
            try
            {
                if (ShouldHarmonyAutoPatch(typeof(FixWorldController).Assembly, ModIdentifier))
                {
                    Harmony.DEBUG = GenCommandLine.CommandLineArgPassed(HarmonyDebugCommandLineArg);
                    HarmonyInst = new Harmony(HarmonyInstanceIdentifier);
                    HarmonyInst.PatchAll(typeof(FixWorldController).Assembly);
                }
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
                throw;
            }
        }

        private void PrepareReflection()
        {
            InjectedDefHasher.PrepareReflection();
            LogWindowExtensions.PrepareReflection();
            OptionsDialogExtensions.PrepareReflection();
        }

        private void RegisterOwnSettings()
        {
            try
            {
                var pack = Settings.GetModSettings(ModIdentifier);
                OwnSettingsPack = pack;
                pack.EntryName = assemblyContentPacks[Assembly.GetCallingAssembly()]?.Name ?? "FixWorld";
                UpdateFeatures.RegisterSettings(pack);
                LogPublisher.RegisterSettings(pack);
            }
            catch (Exception e)
            {
                Logger.ReportException(e);
            }
        }
    }
}
