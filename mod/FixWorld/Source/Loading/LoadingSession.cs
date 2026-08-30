using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    internal readonly struct LoadingSnapshot
    {
        internal readonly LoadingStage Stage;
        internal readonly string StageName;
        internal readonly string StepName;

        internal LoadingSnapshot(LoadingStage stage, string stageName, string stepName)
        {
            Stage = stage;
            StageName = stageName;
            StepName = stepName;
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

        internal LoadingMeasurement(
            double observedMilliseconds,
            IReadOnlyList<LoadingStepMeasurement> steps)
        {
            ObservedMilliseconds = observedMilliseconds;
            Steps = steps;
        }
    }

    internal static class LoadingSession
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<LoadingStep, StepStats> Stats =
            new Dictionary<LoadingStep, StepStats>();

        private static volatile bool active;
        private static bool completed;
        private static long startedAt;
        private static long completedAt;
        private static long sequence;
        private static long currentSequence;
        private static LoadingStage currentStage;
        private static string currentStepName;

        [ThreadStatic]
        private static Stack<Scope> scopes;

        internal static void Start()
        {
            lock (Sync)
            {
                Stats.Clear();
                completed = false;
                startedAt = Stopwatch.GetTimestamp();
                completedAt = 0L;
                currentStage = LoadingStage.Bootstrap;
                currentStepName = "FixWorld attached";
                currentSequence = 0L;
                sequence = 0L;
                active = true;
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
            }
        }

        internal static bool TryComplete()
        {
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
                return true;
            }
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

                snapshot = new LoadingSnapshot(
                    currentStage,
                    GetStageName(currentStage),
                    currentStepName);
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
                return new LoadingMeasurement(
                    ToMilliseconds(Math.Max(0L, end - startedAt)),
                    steps);
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
    }

    internal readonly struct StepDescriptor
    {
        internal readonly LoadingStep Step;
        internal readonly LoadingStage Stage;
        internal readonly string Name;
        internal readonly string DisplayName;

        internal StepDescriptor(
            LoadingStep step,
            LoadingStage stage,
            string name,
            string displayName = null)
        {
            Step = step;
            Stage = stage;
            Name = name;
            DisplayName = displayName ?? name;
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

            if (TryMatchMod(label, TexturePrefix, LoadingStep.LoadTextures, "Load textures", out descriptor) ||
                TryMatchMod(label, AudioPrefix, LoadingStep.LoadAudio, "Load audio", out descriptor) ||
                TryMatchMod(label, StringPrefix, LoadingStep.LoadStrings, "Load strings", out descriptor))
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
            string name,
            out StepDescriptor descriptor)
        {
            if (!StartsWith(label, prefix))
            {
                descriptor = default;
                return false;
            }

            descriptor = new StepDescriptor(
                step,
                LoadingStage.Content,
                name,
                name + ": " + label.Substring(prefix.Length));
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
