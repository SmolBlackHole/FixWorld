using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace FixWorld.Loading
{
    internal enum LoadingThreadAffinity
    {
        MainThread,
        WorkerSafe
    }

    internal enum LoadingExecutionMode
    {
        MainThread,
        Ordered,
        Parallel,
        ParallelThenCommit
    }

    internal enum ModAttributionQuality
    {
        Exact,
        Inferred,
        Global
    }

    internal readonly struct LoadingModAttribution
    {
        internal static readonly LoadingModAttribution Global =
            new LoadingModAttribution("global", "Global", ModAttributionQuality.Global);

        internal readonly string PackageId;
        internal readonly string ModName;
        internal readonly ModAttributionQuality Quality;

        internal LoadingModAttribution(
            string packageId,
            string modName,
            ModAttributionQuality quality)
        {
            PackageId = string.IsNullOrWhiteSpace(packageId) ? "unknown" : packageId;
            ModName = string.IsNullOrWhiteSpace(modName) ? PackageId : modName;
            Quality = quality;
        }

        internal static LoadingModAttribution Exact(ModContentPack mod)
        {
            return Exact(mod.PackageId, mod.Name);
        }

        internal static LoadingModAttribution Exact(
            string packageId,
            string modName)
        {
            return new LoadingModAttribution(
                packageId,
                modName,
                ModAttributionQuality.Exact);
        }
    }

    internal abstract class PreparedLoadingWork
    {
        internal abstract void Commit();
    }

    internal sealed class PreparedLoadingWork<TResult> : PreparedLoadingWork
    {
        private readonly TResult result;
        private readonly Action<TResult> commit;

        internal PreparedLoadingWork(TResult result, Action<TResult> commit)
        {
            this.result = result;
            this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }

        internal override void Commit()
        {
            commit(result);
        }
    }

    internal readonly struct LoadingWorkItem
    {
        internal readonly LoadingStage Stage;
        internal readonly LoadingStep Operation;
        internal readonly string DisplayName;
        internal readonly string Activity;
        internal readonly string ProfilerLabel;
        internal readonly string Subject;
        internal readonly LoadingModAttribution Attribution;
        internal readonly LoadingThreadAffinity Affinity;
        internal readonly int Current;
        internal readonly int Total;
        internal readonly bool ContinueOnFailure;
        internal readonly Action Execute;
        internal readonly Func<PreparedLoadingWork> Prepare;

        internal LoadingWorkItem(
            LoadingStage stage,
            LoadingStep operation,
            string displayName,
            string activity,
            string profilerLabel,
            string subject,
            LoadingModAttribution attribution,
            int current,
            int total,
            bool continueOnFailure,
            Action execute,
            LoadingThreadAffinity affinity = LoadingThreadAffinity.MainThread)
        {
            Stage = stage;
            Operation = operation;
            DisplayName = displayName;
            Activity = activity;
            ProfilerLabel = profilerLabel;
            Subject = subject;
            Attribution = attribution;
            Affinity = affinity;
            Current = current;
            Total = total;
            ContinueOnFailure = continueOnFailure;
            Execute = execute ?? throw new ArgumentNullException(nameof(execute));
            Prepare = null;
        }

        private LoadingWorkItem(
            LoadingStage stage,
            LoadingStep operation,
            string displayName,
            string activity,
            string subject,
            LoadingModAttribution attribution,
            int current,
            int total,
            bool continueOnFailure,
            Func<PreparedLoadingWork> prepare)
        {
            Stage = stage;
            Operation = operation;
            DisplayName = displayName;
            Activity = activity;
            ProfilerLabel = null;
            Subject = subject;
            Attribution = attribution;
            Affinity = LoadingThreadAffinity.WorkerSafe;
            Current = current;
            Total = total;
            ContinueOnFailure = continueOnFailure;
            Execute = null;
            Prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
        }

        internal static LoadingWorkItem CreateParallelThenCommit<TResult>(
            LoadingStage stage,
            LoadingStep operation,
            string displayName,
            string activity,
            string subject,
            LoadingModAttribution attribution,
            int current,
            int total,
            bool continueOnFailure,
            Func<TResult> prepare,
            Action<TResult> commit)
        {
            if (prepare == null)
            {
                throw new ArgumentNullException(nameof(prepare));
            }

            return new LoadingWorkItem(
                stage,
                operation,
                displayName,
                activity,
                subject,
                attribution,
                current,
                total,
                continueOnFailure,
                () => new PreparedLoadingWork<TResult>(prepare(), commit));
        }
    }

    internal readonly struct LoadingPipelineStage
    {
        private static readonly IReadOnlyList<int> NoDependencies = Array.Empty<int>();
        private readonly LoadingWorkItem singleTask;
        private readonly IReadOnlyList<LoadingWorkItem> tasks;

        internal readonly int Id;
        internal readonly string Name;
        internal readonly LoadingStage Phase;
        internal readonly LoadingStep Operation;
        internal readonly LoadingExecutionMode ExecutionMode;
        internal readonly IReadOnlyList<int> Dependencies;

        internal int TaskCount => tasks?.Count ?? 1;

        internal LoadingPipelineStage(
            int id,
            string name,
            LoadingStage phase,
            LoadingStep operation,
            LoadingExecutionMode executionMode,
            LoadingWorkItem task,
            IReadOnlyList<int> dependencies = null)
        {
            Id = id;
            Name = name;
            Phase = phase;
            Operation = operation;
            ExecutionMode = executionMode;
            Dependencies = dependencies ?? NoDependencies;
            singleTask = task;
            tasks = null;
        }

        internal LoadingPipelineStage(
            int id,
            string name,
            LoadingStage phase,
            LoadingStep operation,
            LoadingExecutionMode executionMode,
            IReadOnlyList<LoadingWorkItem> tasks,
            IReadOnlyList<int> dependencies = null)
        {
            if (tasks == null || tasks.Count == 0)
            {
                throw new ArgumentException(
                    "A loading stage must contain at least one task.",
                    nameof(tasks));
            }

            Id = id;
            Name = name;
            Phase = phase;
            Operation = operation;
            ExecutionMode = executionMode;
            Dependencies = dependencies ?? NoDependencies;
            singleTask = default;
            this.tasks = tasks;
        }

        internal LoadingWorkItem GetTask(int index)
        {
            if (tasks != null)
            {
                return tasks[index];
            }

            if (index != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return singleTask;
        }
    }

    internal readonly struct LoadingActionPlan
    {
        private readonly LoadingPipelineStage singleStage;
        private readonly IReadOnlyList<LoadingPipelineStage> stages;

        internal readonly string Label;
        internal readonly LoadingModAttribution Attribution;

        internal int StageCount => stages?.Count ?? 1;

        internal LoadingActionPlan(
            string label,
            LoadingModAttribution attribution,
            LoadingPipelineStage singleStage)
        {
            Label = label;
            Attribution = attribution;
            this.singleStage = singleStage;
            stages = null;
        }

        internal LoadingActionPlan(
            string label,
            LoadingModAttribution attribution,
            IReadOnlyList<LoadingPipelineStage> stages)
        {
            if (stages == null || stages.Count == 0)
            {
                throw new ArgumentException(
                    "A loading action plan must contain at least one stage.",
                    nameof(stages));
            }

            Label = label;
            Attribution = attribution;
            singleStage = default;
            this.stages = stages;
        }

        internal LoadingPipelineStage GetStage(int index)
        {
            if (stages != null)
            {
                return stages[index];
            }

            if (index != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return singleStage;
        }

        internal static LoadingActionPlan CreateFallback(Action action, string label)
        {
            LoadingModAttribution attribution = LoadingAttributionResolver.Infer(action);
            LoadingWorkItem item = new LoadingWorkItem(
                LoadingStage.Content,
                LoadingStep.DelayedInitialization,
                LoaderStepCatalog.GetDisplayName(label),
                null,
                null,
                label,
                attribution,
                1,
                1,
                continueOnFailure: true,
                execute: action);
            LoadingPipelineStage stage = new LoadingPipelineStage(
                0,
                item.DisplayName,
                item.Stage,
                item.Operation,
                LoadingExecutionMode.Ordered,
                item);
            return new LoadingActionPlan(label, attribution, stage);
        }
    }

    internal static class LoadingAttributionResolver
    {
        private static readonly Dictionary<Type, FieldInfo> ModFields =
            new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Assembly, LoadingModAttribution>
            AssemblyAttributions =
                new Dictionary<Assembly, LoadingModAttribution>();

        internal static LoadingModAttribution Infer(Action action)
        {
            ModContentPack targetMod = FindTargetMod(action.Target);
            Assembly assembly = action.Method.DeclaringType?.Assembly ??
                                action.Method.Module.Assembly;
            if (targetMod != null)
            {
                return new LoadingModAttribution(
                    targetMod.PackageId,
                    targetMod.Name,
                    ModAttributionQuality.Inferred);
            }

            if (AssemblyAttributions.TryGetValue(
                    assembly,
                    out LoadingModAttribution attribution))
            {
                return attribution;
            }

            ModContentPack assemblyMod = LoadedModManager.RunningModsListForReading
                .FirstOrDefault(mod => mod.assemblies.loadedAssemblies.Contains(assembly));
            if (assemblyMod != null)
            {
                attribution = new LoadingModAttribution(
                    assemblyMod.PackageId,
                    assemblyMod.Name,
                    ModAttributionQuality.Inferred);
                AssemblyAttributions.Add(assembly, attribution);
                return attribution;
            }

            if (assembly == typeof(LongEventHandler).Assembly)
            {
                attribution = new LoadingModAttribution(
                    ModContentPack.CoreModPackageId,
                    "RimWorld",
                    ModAttributionQuality.Inferred);
                AssemblyAttributions.Add(assembly, attribution);
                return attribution;
            }

            string assemblyName = assembly.GetName().Name ?? "unknown";
            attribution = new LoadingModAttribution(
                assemblyName,
                assemblyName,
                ModAttributionQuality.Inferred);
            AssemblyAttributions.Add(assembly, attribution);
            return attribution;
        }

        private static ModContentPack FindTargetMod(object target)
        {
            if (target is ModContentPack directMod)
            {
                return directMod;
            }

            if (target == null)
            {
                return null;
            }

            Type targetType = target.GetType();
            if (!ModFields.TryGetValue(targetType, out FieldInfo modField))
            {
                modField = targetType.GetFields(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic)
                    .FirstOrDefault(field =>
                        typeof(ModContentPack).IsAssignableFrom(field.FieldType));
                ModFields.Add(targetType, modField);
            }

            return modField?.GetValue(target) as ModContentPack;
        }
    }
}
