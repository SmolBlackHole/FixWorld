using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Verse;

namespace FixWorld.Loading
{
    internal enum LoadingStage
    {
        Bootstrap = 1,
        XmlAndPatches = 2,
        Definitions = 3,
        Content = 4,
        Finalize = 5
    }

    internal enum LoadingStep
    {
        Attached,
        LoadModAssemblies,
        LoadXml,
        CombineXml,
        ParseTranslationKeys,
        CheckPatches,
        ApplyPatches,
        ParseDefinitions,
        ClearPatchCache,
        LoadLanguageMetadata,
        CopyDefinitions,
        ResolveCrossReferences,
        RebindDefinitions,
        BuildLanguageMappings,
        GenerateImpliedDefinitions,
        ResolveDefinitions,
        InitializeRuntime,
        LoadContent,
        LoadAudio,
        LoadTextures,
        LoadStrings,
        LoadAssetBundles,
        LoadBios,
        InjectLanguage,
        RunStaticConstructors,
        BakeAtlases,
        GarbageCollection
    }

    internal enum LoadingContentKind
    {
        None,
        Assemblies,
        Audio,
        Textures,
        Strings
    }

    internal readonly struct LoadingSnapshot
    {
        internal readonly LoadingStage Stage;
        internal readonly string StageName;
        internal readonly string StepName;
        internal readonly double ElapsedMilliseconds;
        internal readonly float Progress;
        internal readonly bool HasDurationEstimate;
        internal readonly double EstimatedTotalMilliseconds;
        internal readonly string CurrentModName;
        internal readonly string CurrentModActivity;
        internal readonly int CurrentModCompletedItems;
        internal readonly int CurrentModTotalItems;

        internal LoadingSnapshot(
            LoadingStage stage,
            string stageName,
            string stepName,
            double elapsedMilliseconds,
            float progress,
            bool hasDurationEstimate,
            double estimatedTotalMilliseconds,
            string currentModName,
            string currentModActivity,
            int currentModCompletedItems,
            int currentModTotalItems)
        {
            Stage = stage;
            StageName = stageName;
            StepName = stepName;
            ElapsedMilliseconds = elapsedMilliseconds;
            Progress = progress;
            HasDurationEstimate = hasDurationEstimate;
            EstimatedTotalMilliseconds = estimatedTotalMilliseconds;
            CurrentModName = currentModName;
            CurrentModActivity = currentModActivity;
            CurrentModCompletedItems = currentModCompletedItems;
            CurrentModTotalItems = currentModTotalItems;
        }
    }

    internal sealed class LoadingStepMeasurement
    {
        internal LoadingStep Step { get; }
        internal LoadingStage Stage { get; }
        internal string Name { get; }
        internal long Calls { get; }
        internal double TotalMilliseconds { get; }
        internal double ExclusiveMilliseconds { get; }
        internal double MainThreadMilliseconds { get; }
        internal double WorkerThreadMilliseconds { get; }
        internal double MainThreadExclusiveMilliseconds { get; }
        internal double WorkerThreadExclusiveMilliseconds { get; }

        internal LoadingStepMeasurement(
            LoadingStep step,
            LoadingStage stage,
            string name,
            long calls,
            double totalMilliseconds,
            double exclusiveMilliseconds,
            double mainThreadMilliseconds,
            double workerThreadMilliseconds,
            double mainThreadExclusiveMilliseconds,
            double workerThreadExclusiveMilliseconds)
        {
            Step = step;
            Stage = stage;
            Name = name;
            Calls = calls;
            TotalMilliseconds = totalMilliseconds;
            ExclusiveMilliseconds = exclusiveMilliseconds;
            MainThreadMilliseconds = mainThreadMilliseconds;
            WorkerThreadMilliseconds = workerThreadMilliseconds;
            MainThreadExclusiveMilliseconds = mainThreadExclusiveMilliseconds;
            WorkerThreadExclusiveMilliseconds = workerThreadExclusiveMilliseconds;
        }
    }

    internal sealed class LoadingMeasurement
    {
        internal double ObservedMilliseconds { get; }
        internal IReadOnlyList<LoadingStepMeasurement> Steps { get; }
        internal IReadOnlyList<ModAssemblyMeasurement> ModAssemblies { get; }

        internal LoadingMeasurement(
            double observedMilliseconds,
            IReadOnlyList<LoadingStepMeasurement> steps,
            IReadOnlyList<ModAssemblyMeasurement> modAssemblies)
        {
            ObservedMilliseconds = observedMilliseconds;
            Steps = steps;
            ModAssemblies = modAssemblies;
        }
    }

    internal sealed class ModAssemblyMeasurement
    {
        internal string PackageId { get; }
        internal string ModName { get; }
        internal int Files { get; }
        internal int Loaded { get; }
        internal int Failed { get; }
        internal int Reflected { get; }
        internal int Unusable { get; }
        internal double TotalMilliseconds { get; }
        internal double LoadMilliseconds { get; }
        internal double ReflectionMilliseconds { get; }

        internal ModAssemblyMeasurement(
            string packageId,
            string modName,
            int files,
            int loaded,
            int failed,
            int reflected,
            int unusable,
            double totalMilliseconds,
            double loadMilliseconds,
            double reflectionMilliseconds)
        {
            PackageId = packageId;
            ModName = modName;
            Files = files;
            Loaded = loaded;
            Failed = failed;
            Reflected = reflected;
            Unusable = unusable;
            TotalMilliseconds = totalMilliseconds;
            LoadMilliseconds = loadMilliseconds;
            ReflectionMilliseconds = reflectionMilliseconds;
        }
    }

    internal static class LoadingSession
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<LoadingStep, StepStats> Stats =
            new Dictionary<LoadingStep, StepStats>();
        private static readonly Dictionary<string, ModAssemblyStats> ModAssemblyStatsByPackage =
            new Dictionary<string, ModAssemblyStats>(StringComparer.OrdinalIgnoreCase);
        private static readonly StepDescriptor ModAssemblyDescriptor = new StepDescriptor(
            LoadingStep.LoadModAssemblies,
            LoadingStage.Bootstrap,
            "Load mod assemblies");

        private static volatile bool active;
        private static bool completed;
        private static long startedAt;
        private static long completedAt;
        private static long sequence;
        private static long currentSequence;
        private static LoadingStage currentStage;
        private static string currentStepName;
        private static double estimatedDurationMilliseconds;
        private static long currentModSequence;
        private static LoadingContentKind currentModContentKind;
        private static string currentModName;
        private static string currentModActivity;
        private static int currentModCompletedItems;
        private static int currentModTotalItems;

        [ThreadStatic]
        private static Stack<Scope> scopes;

        [ThreadStatic]
        private static ModAssemblyScope modAssemblyScope;

        internal static void Start(bool readEstimate)
        {
            double previousDuration = readEstimate ? LoadingEstimateStore.Read() : 0.0;
            lock (Sync)
            {
                if (active || startedAt != 0L)
                {
                    return;
                }

                Stats.Clear();
                ModAssemblyStatsByPackage.Clear();
                completed = false;
                startedAt = Stopwatch.GetTimestamp();
                completedAt = 0L;
                currentStage = LoadingStage.Bootstrap;
                currentStepName = "FixWorld attached";
                currentSequence = 0L;
                sequence = 0L;
                estimatedDurationMilliseconds = previousDuration;
                ClearCurrentMod();
                active = true;
            }
        }

        internal static void LoadEstimate()
        {
            double previousDuration = LoadingEstimateStore.Read();
            lock (Sync)
            {
                estimatedDurationMilliseconds = previousDuration;
            }
        }

        internal static void BeginModAssemblies(ModContentPack mod)
        {
            if (!active || mod == null)
            {
                return;
            }

            long scopeSequence = Interlocked.Increment(ref sequence);
            ModAssemblyScope scope = new ModAssemblyScope(
                mod.PackageId,
                mod.Name,
                scopeSequence,
                Stopwatch.GetTimestamp(),
                UnityData.IsInMainThread);
            modAssemblyScope = scope;
            lock (Sync)
            {
                if (!active)
                {
                    return;
                }

                if (!ModAssemblyStatsByPackage.TryGetValue(
                        scope.PackageId,
                        out ModAssemblyStats stats))
                {
                    stats = new ModAssemblyStats(scope.PackageId, scope.ModName);
                    ModAssemblyStatsByPackage.Add(scope.PackageId, stats);
                }

                stats.Calls++;
                currentSequence = scopeSequence;
                currentStage = LoadingStage.Bootstrap;
                currentStepName = "Load mod assemblies: " + scope.ModName;
                currentModSequence = scopeSequence;
                currentModContentKind = LoadingContentKind.Assemblies;
                currentModName = scope.ModName;
                currentModActivity = "Assemblies";
                currentModCompletedItems = 0;
                currentModTotalItems = -1;
            }
        }

        internal static void SetCurrentModAssemblyTotal(ModContentPack mod, int totalFiles)
        {
            ModAssemblyScope scope = modAssemblyScope;
            if (!active || scope == null || mod == null ||
                !string.Equals(scope.PackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (Sync)
            {
                scope.TotalFiles = Math.Max(0, totalFiles);
                currentModTotalItems = scope.TotalFiles;
                if (ModAssemblyStatsByPackage.TryGetValue(
                        scope.PackageId,
                        out ModAssemblyStats stats))
                {
                    stats.Files = Math.Max(stats.Files, scope.TotalFiles);
                }
            }
        }

        internal static long BeginAssemblyFileLoad(string path)
        {
            ModAssemblyScope scope = modAssemblyScope;
            if (!active || scope == null)
            {
                return 0L;
            }

            lock (Sync)
            {
                currentModActivity = "Assembly: " + Path.GetFileName(path);
            }

            return Stopwatch.GetTimestamp();
        }

        internal static void EndAssemblyFileLoad(long startedAt, bool loaded)
        {
            ModAssemblyScope scope = modAssemblyScope;
            if (startedAt == 0L || scope == null)
            {
                return;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            lock (Sync)
            {
                if (ModAssemblyStatsByPackage.TryGetValue(
                        scope.PackageId,
                        out ModAssemblyStats stats))
                {
                    stats.LoadTicks += elapsedTicks;
                    if (loaded)
                    {
                        stats.Loaded++;
                    }
                    else
                    {
                        stats.Failed++;
                    }
                }

                currentModCompletedItems++;
                if (currentModTotalItems >= 0)
                {
                    currentModCompletedItems = Math.Min(
                        currentModCompletedItems,
                        currentModTotalItems);
                }

                currentModActivity = "Assemblies";
            }
        }

        internal static void ObserveAssemblyReflection(
            Assembly assembly,
            long startedAt,
            bool usable)
        {
            ModAssemblyScope scope = modAssemblyScope;
            if (!active || scope == null || startedAt == 0L)
            {
                return;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            lock (Sync)
            {
                if (ModAssemblyStatsByPackage.TryGetValue(
                        scope.PackageId,
                        out ModAssemblyStats stats))
                {
                    stats.ReflectionTicks += elapsedTicks;
                    stats.Reflected++;
                    if (!usable)
                    {
                        stats.Unusable++;
                    }
                }

                currentModActivity = assembly == null
                    ? "Assembly reflection"
                    : "Reflection: " + assembly.GetName().Name;
            }
        }

        internal static void EndModAssemblies()
        {
            ModAssemblyScope scope = modAssemblyScope;
            modAssemblyScope = null;
            if (scope == null)
            {
                return;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - scope.StartedAt;
            lock (Sync)
            {
                if (ModAssemblyStatsByPackage.TryGetValue(
                        scope.PackageId,
                        out ModAssemblyStats assemblyStats))
                {
                    assemblyStats.TotalTicks += elapsedTicks;
                }

                if (!Stats.TryGetValue(LoadingStep.LoadModAssemblies, out StepStats stats))
                {
                    stats = new StepStats(ModAssemblyDescriptor);
                    Stats.Add(LoadingStep.LoadModAssemblies, stats);
                }

                stats.Calls++;
                stats.TotalTicks += elapsedTicks;
                stats.ExclusiveTicks += elapsedTicks;
                if (scope.MainThread)
                {
                    stats.MainThreadTicks += elapsedTicks;
                    stats.MainThreadExclusiveTicks += elapsedTicks;
                }
                else
                {
                    stats.WorkerThreadTicks += elapsedTicks;
                    stats.WorkerThreadExclusiveTicks += elapsedTicks;
                }

                if (currentModSequence == scope.Sequence)
                {
                    currentSequence = 0L;
                    currentStepName = GetStageName(LoadingStage.Bootstrap);
                    ClearCurrentMod();
                }
            }
        }

        internal static void Begin(string label)
        {
            if (!active)
            {
                return;
            }

            if (scopes == null)
            {
                scopes = new Stack<Scope>();
            }

            bool recognized = LoaderStepCatalog.TryMatch(label, out StepDescriptor descriptor);
            long scopeSequence = recognized ? Interlocked.Increment(ref sequence) : 0L;
            Scope scope = new Scope(
                Stopwatch.GetTimestamp(),
                recognized,
                descriptor,
                scopeSequence,
                UnityData.IsInMainThread);
            scopes.Push(scope);

            if (!recognized)
            {
                return;
            }

            lock (Sync)
            {
                if (!active)
                {
                    return;
                }

                currentSequence = scopeSequence;
                currentStage = descriptor.Stage;
                currentStepName = descriptor.DisplayName;
                if (descriptor.ModName != null)
                {
                    SetCurrentMod(scopeSequence, descriptor);
                }
            }
        }

        internal static void SetCurrentModItemTotal(LoadingContentKind kind, int totalItems)
        {
            lock (Sync)
            {
                if (!active || currentModContentKind != kind)
                {
                    return;
                }

                currentModCompletedItems = 0;
                currentModTotalItems = Math.Max(0, totalItems);
            }
        }

        internal static void AdvanceCurrentModItem()
        {
            lock (Sync)
            {
                if (!active || currentModTotalItems < 0)
                {
                    return;
                }

                currentModCompletedItems = Math.Min(
                    currentModCompletedItems + 1,
                    currentModTotalItems);
            }
        }

        internal static void End()
        {
            if (!active || scopes == null || scopes.Count == 0)
            {
                return;
            }

            Scope scope = scopes.Pop();
            long elapsedTicks = Stopwatch.GetTimestamp() - scope.StartedAt;
            long exclusiveTicks = Math.Max(0L, elapsedTicks - scope.ChildTicks);
            if (scopes.Count > 0)
            {
                scopes.Peek().ChildTicks += elapsedTicks;
            }

            if (!scope.Recognized)
            {
                return;
            }

            lock (Sync)
            {
                if (!Stats.TryGetValue(scope.Descriptor.Step, out StepStats stats))
                {
                    stats = new StepStats(scope.Descriptor);
                    Stats.Add(scope.Descriptor.Step, stats);
                }

                stats.Calls++;
                stats.TotalTicks += elapsedTicks;
                stats.ExclusiveTicks += exclusiveTicks;
                if (scope.MainThread)
                {
                    stats.MainThreadTicks += elapsedTicks;
                    stats.MainThreadExclusiveTicks += exclusiveTicks;
                }
                else
                {
                    stats.WorkerThreadTicks += elapsedTicks;
                    stats.WorkerThreadExclusiveTicks += exclusiveTicks;
                }

                if (currentSequence != scope.Sequence)
                {
                    RestoreCurrentModAfter(scope);
                    return;
                }

                Scope parent = scopes.FirstOrDefault(candidate => candidate.Recognized);
                if (parent != null)
                {
                    currentSequence = parent.Sequence;
                    currentStage = parent.Descriptor.Stage;
                    currentStepName = parent.Descriptor.DisplayName;
                }
                else
                {
                    currentSequence = 0L;
                    currentStepName = GetStageName(scope.Descriptor.Stage);
                }

                RestoreCurrentModAfter(scope);
            }
        }

        internal static bool TryComplete()
        {
            double observedMilliseconds;
            lock (Sync)
            {
                if (completed)
                {
                    return false;
                }

                completed = true;
                completedAt = Stopwatch.GetTimestamp();
                active = false;
                currentSequence = 0L;
                currentStage = LoadingStage.Finalize;
                currentStepName = "Ready";
                observedMilliseconds = ToMilliseconds(completedAt - startedAt);
            }

            LoadingEstimateStore.Write(observedMilliseconds);
            return true;
        }

        internal static bool TryGetSnapshot(out LoadingSnapshot snapshot)
        {
            lock (Sync)
            {
                if (!active)
                {
                    snapshot = default;
                    return false;
                }

                double elapsedMilliseconds =
                    ToMilliseconds(Math.Max(0L, Stopwatch.GetTimestamp() - startedAt));
                bool hasEstimate = estimatedDurationMilliseconds > 0.0;
                float progress = hasEstimate
                    ? (float)Math.Min(0.98, elapsedMilliseconds / estimatedDurationMilliseconds)
                    : (float)Math.Min(0.95, ((int)currentStage - 0.5) / 5.0);
                snapshot = new LoadingSnapshot(
                    currentStage,
                    GetStageName(currentStage),
                    currentStepName,
                    elapsedMilliseconds,
                    Math.Max(0.02f, progress),
                    hasEstimate,
                    estimatedDurationMilliseconds,
                    currentModName,
                    currentModActivity,
                    currentModCompletedItems,
                    currentModTotalItems);
                return true;
            }
        }

        internal static LoadingMeasurement GetMeasurement()
        {
            lock (Sync)
            {
                long end = completedAt != 0L ? completedAt : Stopwatch.GetTimestamp();
                List<LoadingStepMeasurement> steps = Stats.Values
                    .OrderBy(item => item.Descriptor.Stage)
                    .ThenBy(item => item.Descriptor.Step)
                    .Select(item => new LoadingStepMeasurement(
                        item.Descriptor.Step,
                        item.Descriptor.Stage,
                        item.Descriptor.Name,
                        item.Calls,
                        ToMilliseconds(item.TotalTicks),
                        ToMilliseconds(item.ExclusiveTicks),
                        ToMilliseconds(item.MainThreadTicks),
                        ToMilliseconds(item.WorkerThreadTicks),
                        ToMilliseconds(item.MainThreadExclusiveTicks),
                        ToMilliseconds(item.WorkerThreadExclusiveTicks)))
                    .ToList();
                List<ModAssemblyMeasurement> modAssemblies = ModAssemblyStatsByPackage.Values
                    .OrderByDescending(item => item.TotalTicks)
                    .ThenBy(item => item.ModName, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new ModAssemblyMeasurement(
                        item.PackageId,
                        item.ModName,
                        item.Files,
                        item.Loaded,
                        item.Failed,
                        item.Reflected,
                        item.Unusable,
                        ToMilliseconds(item.TotalTicks),
                        ToMilliseconds(item.LoadTicks),
                        ToMilliseconds(item.ReflectionTicks)))
                    .ToList();
                return new LoadingMeasurement(
                    ToMilliseconds(Math.Max(0L, end - startedAt)),
                    steps,
                    modAssemblies);
            }
        }

        internal static string GetStageName(LoadingStage stage)
        {
            switch (stage)
            {
                case LoadingStage.Bootstrap: return "Bootstrap";
                case LoadingStage.XmlAndPatches: return "XML & patches";
                case LoadingStage.Definitions: return "Definitions";
                case LoadingStage.Content: return "Content";
                case LoadingStage.Finalize: return "Finalize";
                default: throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void RestoreCurrentModAfter(Scope completedScope)
        {
            if (currentModSequence != completedScope.Sequence)
            {
                return;
            }

            Scope parent = scopes.FirstOrDefault(candidate =>
                candidate.Recognized && candidate.Descriptor.ModName != null);
            if (parent == null)
            {
                ClearCurrentMod();
                return;
            }

            SetCurrentMod(parent.Sequence, parent.Descriptor);
        }

        private static void SetCurrentMod(long scopeSequence, StepDescriptor descriptor)
        {
            currentModSequence = scopeSequence;
            currentModContentKind = descriptor.ContentKind;
            currentModName = descriptor.ModName;
            currentModActivity = descriptor.ModActivity;
            currentModCompletedItems = 0;
            currentModTotalItems = -1;
        }

        private static void ClearCurrentMod()
        {
            currentModSequence = 0L;
            currentModContentKind = LoadingContentKind.None;
            currentModName = null;
            currentModActivity = null;
            currentModCompletedItems = 0;
            currentModTotalItems = -1;
        }

        private sealed class Scope
        {
            internal readonly long StartedAt;
            internal readonly bool Recognized;
            internal readonly StepDescriptor Descriptor;
            internal readonly long Sequence;
            internal readonly bool MainThread;
            internal long ChildTicks;

            internal Scope(
                long startedAt,
                bool recognized,
                StepDescriptor descriptor,
                long sequence,
                bool mainThread)
            {
                StartedAt = startedAt;
                Recognized = recognized;
                Descriptor = descriptor;
                Sequence = sequence;
                MainThread = mainThread;
            }
        }

        private sealed class StepStats
        {
            internal readonly StepDescriptor Descriptor;
            internal long Calls;
            internal long TotalTicks;
            internal long ExclusiveTicks;
            internal long MainThreadTicks;
            internal long WorkerThreadTicks;
            internal long MainThreadExclusiveTicks;
            internal long WorkerThreadExclusiveTicks;

            internal StepStats(StepDescriptor descriptor)
            {
                Descriptor = descriptor;
            }
        }

        private sealed class ModAssemblyScope
        {
            internal readonly string PackageId;
            internal readonly string ModName;
            internal readonly long Sequence;
            internal readonly long StartedAt;
            internal readonly bool MainThread;
            internal int TotalFiles;

            internal ModAssemblyScope(
                string packageId,
                string modName,
                long sequence,
                long startedAt,
                bool mainThread)
            {
                PackageId = packageId;
                ModName = modName;
                Sequence = sequence;
                StartedAt = startedAt;
                MainThread = mainThread;
                TotalFiles = -1;
            }
        }

        private sealed class ModAssemblyStats
        {
            internal readonly string PackageId;
            internal readonly string ModName;
            internal long Calls;
            internal int Files;
            internal int Loaded;
            internal int Failed;
            internal int Reflected;
            internal int Unusable;
            internal long TotalTicks;
            internal long LoadTicks;
            internal long ReflectionTicks;

            internal ModAssemblyStats(string packageId, string modName)
            {
                PackageId = packageId;
                ModName = modName;
            }
        }

    }

    internal readonly struct StepDescriptor
    {
        internal readonly LoadingStep Step;
        internal readonly LoadingStage Stage;
        internal readonly string Name;
        internal readonly string DisplayName;
        internal readonly LoadingContentKind ContentKind;
        internal readonly string ModName;
        internal readonly string ModActivity;

        internal StepDescriptor(
            LoadingStep step,
            LoadingStage stage,
            string name,
            string displayName = null,
            LoadingContentKind contentKind = LoadingContentKind.None,
            string modName = null,
            string modActivity = null)
        {
            Step = step;
            Stage = stage;
            Name = name;
            DisplayName = displayName ?? name;
            ContentKind = contentKind;
            ModName = modName;
            ModActivity = modActivity;
        }
    }

    internal static class LoaderStepCatalog
    {
        private const string TexturePrefix = "Loading assets of type UnityEngine.Texture2D for mod ";
        private const string AudioPrefix = "Loading assets of type UnityEngine.AudioClip for mod ";
        private const string StringPrefix = "Loading assets of type System.String for mod ";

        internal static bool TryMatch(string label, out StepDescriptor descriptor)
        {
            switch (label)
            {
                case "LoadModXML()":
                    descriptor = Step(LoadingStep.LoadXml, LoadingStage.XmlAndPatches, "Load XML");
                    return true;
                case "CombineIntoUnifiedXML()":
                    descriptor = Step(LoadingStep.CombineXml, LoadingStage.XmlAndPatches, "Combine XML");
                    return true;
                case "TKeySystem.Parse()":
                    descriptor = Step(LoadingStep.ParseTranslationKeys, LoadingStage.XmlAndPatches, "Parse translation keys");
                    return true;
                case "ErrorCheckPatches()":
                    descriptor = Step(LoadingStep.CheckPatches, LoadingStage.XmlAndPatches, "Check patches");
                    return true;
                case "ApplyPatches()":
                    descriptor = Step(LoadingStep.ApplyPatches, LoadingStage.XmlAndPatches, "Apply patches");
                    return true;
                case "ParseAndProcessXML()":
                    descriptor = Step(LoadingStep.ParseDefinitions, LoadingStage.XmlAndPatches, "Parse definitions");
                    return true;
                case "ClearCachedPatches()":
                    descriptor = Step(LoadingStep.ClearPatchCache, LoadingStage.XmlAndPatches, "Clear patch cache");
                    return true;
                case "Load language metadata.":
                    descriptor = Step(LoadingStep.LoadLanguageMetadata, LoadingStage.Definitions, "Load language metadata");
                    return true;
                case "Copy all Defs from mods to global databases.":
                    descriptor = Step(LoadingStep.CopyDefinitions, LoadingStage.Definitions, "Copy definitions");
                    return true;
                case "TKeySystem.BuildMappings()":
                    descriptor = Step(LoadingStep.BuildLanguageMappings, LoadingStage.Definitions, "Build language mappings");
                    return true;
                case "Resolve references.":
                    descriptor = Step(LoadingStep.ResolveDefinitions, LoadingStage.Definitions, "Resolve definitions");
                    return true;
                case "Load keyboard preferences.":
                case "Short hash giving.":
                    descriptor = Step(LoadingStep.InitializeRuntime, LoadingStage.Definitions, "Initialize runtime");
                    return true;
                case "LoadModContent":
                    descriptor = Step(LoadingStep.LoadContent, LoadingStage.Content, "Load mod content");
                    return true;
                case "Reload audio clips":
                    descriptor = Step(LoadingStep.LoadAudio, LoadingStage.Content, "Load audio");
                    return true;
                case "Reload textures":
                    descriptor = Step(LoadingStep.LoadTextures, LoadingStage.Content, "Load textures");
                    return true;
                case "Reload strings":
                    descriptor = Step(LoadingStep.LoadStrings, LoadingStage.Content, "Load strings");
                    return true;
                case "Reload asset bundles":
                    descriptor = Step(LoadingStep.LoadAssetBundles, LoadingStage.Content, "Load asset bundles");
                    return true;
                case "Load all bios":
                    descriptor = Step(LoadingStep.LoadBios, LoadingStage.Finalize, "Load bios");
                    return true;
                case "Inject selected language data into game data.":
                    descriptor = Step(LoadingStep.InjectLanguage, LoadingStage.Finalize, "Inject language data");
                    return true;
                case "Static constructor calls":
                    descriptor = Step(LoadingStep.RunStaticConstructors, LoadingStage.Finalize, "Run static constructors");
                    return true;
                case "Atlas baking.":
                    descriptor = Step(LoadingStep.BakeAtlases, LoadingStage.Finalize, "Bake atlases");
                    return true;
                case "Garbage Collection":
                    descriptor = Step(LoadingStep.GarbageCollection, LoadingStage.Finalize, "Clean up");
                    return true;
            }

            if (StartsWith(label, "Resolve cross-references"))
            {
                descriptor = Step(LoadingStep.ResolveCrossReferences, LoadingStage.Definitions, "Resolve cross-references");
                return true;
            }

            if (StartsWith(label, "Rebind DefOfs"))
            {
                descriptor = Step(LoadingStep.RebindDefinitions, LoadingStage.Definitions, "Rebind definitions");
                return true;
            }

            if (StartsWith(label, "Generate implied Defs"))
            {
                descriptor = Step(LoadingStep.GenerateImpliedDefinitions, LoadingStage.Definitions, "Generate implied definitions");
                return true;
            }

            if (StartsWith(label, "Other def binding"))
            {
                descriptor = Step(LoadingStep.ResolveDefinitions, LoadingStage.Definitions, "Bind definitions");
                return true;
            }

            if (TryMatchMod(
                    label,
                    TexturePrefix,
                    LoadingStep.LoadTextures,
                    LoadingContentKind.Textures,
                    "Load textures",
                    "Textures",
                    out descriptor) ||
                TryMatchMod(
                    label,
                    AudioPrefix,
                    LoadingStep.LoadAudio,
                    LoadingContentKind.Audio,
                    "Load audio",
                    "Audio",
                    out descriptor) ||
                TryMatchMod(
                    label,
                    StringPrefix,
                    LoadingStep.LoadStrings,
                    LoadingContentKind.Strings,
                    "Load strings",
                    "Strings",
                    out descriptor))
            {
                return true;
            }

            if (TryMatchModContent(label, out descriptor))
            {
                return true;
            }

            descriptor = default;
            return false;
        }

        private static bool TryMatchMod(
            string label,
            string prefix,
            LoadingStep step,
            LoadingContentKind contentKind,
            string name,
            string activity,
            out StepDescriptor descriptor)
        {
            if (!StartsWith(label, prefix))
            {
                descriptor = default;
                return false;
            }

            string modName = label.Substring(prefix.Length);
            descriptor = new StepDescriptor(
                step,
                LoadingStage.Content,
                name,
                name + ": " + modName,
                contentKind,
                modName,
                activity);
            return true;
        }

        private static bool TryMatchModContent(string label, out StepDescriptor descriptor)
        {
            const string prefix = "Loading ";
            const string suffix = " content";
            if (!StartsWith(label, prefix) ||
                !label.EndsWith(suffix, StringComparison.Ordinal) ||
                label.Length <= prefix.Length + suffix.Length)
            {
                descriptor = default;
                return false;
            }

            string modName = label.Substring(
                prefix.Length,
                label.Length - prefix.Length - suffix.Length);
            descriptor = new StepDescriptor(
                LoadingStep.LoadContent,
                LoadingStage.Content,
                "Load mod content",
                "Load mod content: " + modName,
                LoadingContentKind.None,
                modName,
                "Mod content");
            return true;
        }

        private static bool StartsWith(string value, string prefix)
        {
            return value != null && value.StartsWith(prefix, StringComparison.Ordinal);
        }

        private static StepDescriptor Step(LoadingStep step, LoadingStage stage, string name)
        {
            return new StepDescriptor(step, stage, name);
        }
    }
}
