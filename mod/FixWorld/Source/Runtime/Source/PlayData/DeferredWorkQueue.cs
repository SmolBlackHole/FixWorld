using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using FixWorld.Profiling;
using Verse;

namespace FixWorld.PlayData
{
    internal sealed class DeferredWorkQueue
    {
        private static readonly long FrameBudgetTicks =
            Stopwatch.Frequency / 5;
        private static readonly long ProgressIntervalTicks =
            Stopwatch.Frequency / 10;

        private readonly object sync = new object();
        private readonly List<DeferredWorkItem> pending =
            new List<DeferredWorkItem>();
        private readonly Dictionary<string, DeferredWorkDescriptor> descriptors =
            new Dictionary<string, DeferredWorkDescriptor>(StringComparer.Ordinal);
        private Profiler<string> runtimes =
            new Profiler<string>(StringComparer.Ordinal);
        private Profiler<string> waits =
            new Profiler<string>(StringComparer.Ordinal);
        private bool capturing;

        internal void BeginCapture()
        {
            lock (sync)
            {
                if (capturing)
                {
                    throw new InvalidOperationException(
                        "Deferred work capture is already active.");
                }

                pending.Clear();
                descriptors.Clear();
                runtimes = new Profiler<string>(StringComparer.Ordinal);
                waits = new Profiler<string>(StringComparer.Ordinal);
                capturing = true;
            }
        }

        internal bool TryCapture(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (sync)
            {
                if (!capturing)
                {
                    return false;
                }

                Add(action, GetLabel(action), GetOwner(action));
                return true;
            }
        }

        internal void Schedule(
            PlayDataStageRunner stages,
            Action completed,
            Action<Exception> failed)
        {
            if (stages == null)
            {
                throw new ArgumentNullException(nameof(stages));
            }

            DeferredWorkItem[] work;
            lock (sync)
            {
                if (!capturing)
                {
                    throw new InvalidOperationException(
                        "Deferred work capture is not active.");
                }

                capturing = false;
                Add(
                    RimWorldPlayData.LoadBios,
                    "Load all bios",
                    "ludeon.rimworld");
                Add(
                    RimWorldPlayData.InjectLanguage,
                    "Inject selected language data",
                    "ludeon.rimworld");
                if (RimWorldPlayData.TryGetSafeStaticConstructorPostfix(
                        out MethodInfo staticConstructorPostfix,
                        out string staticConstructorPostfixOwner,
                        out string blockedStaticConstructorOwners))
                {
                    foreach (Type type in
                             RimWorldPlayData.GetStaticConstructorTypes())
                    {
                        Type constructorType = type;
                        Add(
                            () => RimWorldPlayData.RunStaticConstructor(
                                constructorType),
                            "Static constructor: " + constructorType.FullName,
                            GetTypeOwner(constructorType));
                    }

                    Add(
                        RimWorldPlayData.CompleteStaticConstructors,
                        "Complete static constructors",
                        "ludeon.rimworld");
                    if (staticConstructorPostfix != null)
                    {
                        MethodInfo postfix = staticConstructorPostfix;
                        Add(
                            () => RimWorldPlayData
                                .InvokeStaticConstructorPostfix(postfix),
                            "CallAll postfix: " +
                            staticConstructorPostfix.DeclaringType?.FullName +
                            "." + staticConstructorPostfix.Name,
                            staticConstructorPostfixOwner);
                    }

                    if (Prefs.DevMode)
                    {
                        Add(
                            RimWorldPlayData
                                .ReportMissingStaticConstructorAttributes,
                            "Report missing static constructor attributes",
                            "ludeon.rimworld");
                    }
                }
                else
                {
                    Log.Message(
                        "[FixWorld] Static constructor splitting disabled; " +
                        "CallAll() patches=" + blockedStaticConstructorOwners +
                        ".");
                    Add(
                        RimWorldPlayData.RunPatchedStaticConstructors,
                        "Run patched static constructors",
                        blockedStaticConstructorOwners);
                }
                Add(
                    RimWorldPlayData.InitializeFloatMenus,
                    "Initialize float menus",
                    "ludeon.rimworld");
                Add(
                    RimWorldPlayData.BakeStaticAtlases,
                    "Bake static atlases",
                    "ludeon.rimworld");
                Add(
                    RimWorldPlayData.CollectUnusedAssets,
                    "Collect unused assets",
                    "ludeon.rimworld");
                Add(
                    Log.ResetMessageCount,
                    "Reset message count",
                    "ludeon.rimworld");
                work = pending.ToArray();
                pending.Clear();
            }

            LongEventHandler.QueueLongEvent(
                Run(work, stages, completed, failed),
                null,
                failed,
                showExtraUIInfo: false,
                forceHideUI: false);
        }

        internal void Abort()
        {
            lock (sync)
            {
                capturing = false;
                pending.Clear();
            }
        }

        internal DeferredWorkSnapshot GetSnapshot()
        {
            Dictionary<string, DeferredWorkDescriptor> current;
            lock (sync)
            {
                current = new Dictionary<string, DeferredWorkDescriptor>(
                    descriptors,
                    StringComparer.Ordinal);
            }

            ProfileSnapshot<string> runtimeSnapshot = runtimes.Snapshot();
            ProfileSnapshot<string> waitSnapshot = waits.Snapshot();
            List<DeferredWorkMeasurement> measurements =
                new List<DeferredWorkMeasurement>(runtimeSnapshot.Count);
            foreach (ProfileMeasurement<string> runtime in runtimeSnapshot)
            {
                if (!current.TryGetValue(
                        runtime.Key,
                        out DeferredWorkDescriptor descriptor))
                {
                    continue;
                }

                waitSnapshot.TryGet(
                    runtime.Key,
                    out ProfileMeasurement<string> wait);
                measurements.Add(new DeferredWorkMeasurement(
                    descriptor.Owner,
                    descriptor.Name,
                    runtime.Calls,
                    runtime.Failures,
                    runtime.TotalTime,
                    runtime.MaximumTime,
                    wait?.AverageTime ?? TimeSpan.Zero,
                    wait?.MaximumTime ?? TimeSpan.Zero));
            }

            return new DeferredWorkSnapshot(measurements);
        }

        private IEnumerable Run(
            IReadOnlyList<DeferredWorkItem> work,
            PlayDataStageRunner stages,
            Action completed,
            Action<Exception> failed)
        {
            using (PlayDataStageOperation operation =
                   stages.Begin(PlayDataLoadStage.DeferredMainThreadWork))
            {
                bool outerProfile = work.Count > 0;
                if (outerProfile)
                {
                    DeepProfiler.Start("ExecuteToExecuteWhenFinished()");
                }

                try
                {
                    long frameStartedAt = Stopwatch.GetTimestamp();
                    long lastProgressAt = 0L;
                    for (int index = 0; index < work.Count; index++)
                    {
                        DeferredWorkItem item = work[index];
                        long startedAt = Stopwatch.GetTimestamp();
                        if (index == 0 ||
                            startedAt - lastProgressAt >= ProgressIntervalTicks)
                        {
                            operation.Report(item.Name, index, work.Count);
                            LongEventHandler.SetCurrentEventText(
                                "FixWorld: " + item.Name);
                            lastProgressAt = startedAt;
                        }

                        waits.Observe(
                            item.Key,
                            ToTimeSpan(startedAt - item.EnqueuedAt));
                        bool succeeded = false;
                        try
                        {
                            item.Execute();
                            succeeded = true;
                        }
                        catch (Exception exception)
                        {
                            Log.Error(
                                "Could not execute post-long-event action. " +
                                "Exception: " + exception);
                        }
                        finally
                        {
                            runtimes.Observe(
                                item.Key,
                                ToTimeSpan(
                                    Stopwatch.GetTimestamp() - startedAt),
                                succeeded);
                        }

                        if (index + 1 < work.Count &&
                            Stopwatch.GetTimestamp() - frameStartedAt >=
                            FrameBudgetTicks)
                        {
                            yield return null;
                            frameStartedAt = Stopwatch.GetTimestamp();
                        }
                    }

                    operation.Complete();
                }
                finally
                {
                    if (outerProfile)
                    {
                        DeepProfiler.End();
                    }
                }
            }

            try
            {
                stages.Run(PlayDataLoadStage.Complete, completed);
            }
            catch (Exception exception)
            {
                failed?.Invoke(exception);
                throw;
            }
        }

        private static string GetLabel(Action action)
        {
            string declaringType = action.Method.DeclaringType?.ToString() ??
                                   "<dynamic>";
            return declaringType + " -> " + action.Method;
        }

        private void Add(Action action, string name, string owner)
        {
            string key = owner + "\n" + name;
            pending.Add(new DeferredWorkItem(
                key,
                name,
                action,
                Stopwatch.GetTimestamp()));
            descriptors[key] = new DeferredWorkDescriptor(owner, name);
        }

        private static string GetOwner(Action action)
        {
            string assembly = action.Method.DeclaringType?.Assembly
                                  .GetName()
                                  .Name ?? "global";
            object target = action.Target;
            if (target == null)
            {
                return assembly;
            }

            try
            {
                if (target is ModContentPack direct)
                {
                    return direct.PackageId;
                }

                if (target is Def directDef && directDef.modContentPack != null)
                {
                    return directDef.modContentPack.PackageId;
                }

                foreach (FieldInfo field in target.GetType().GetFields(
                             BindingFlags.Instance |
                             BindingFlags.Public |
                             BindingFlags.NonPublic))
                {
                    if (typeof(ModContentPack).IsAssignableFrom(field.FieldType) &&
                        field.GetValue(target) is ModContentPack mod)
                    {
                        return mod.PackageId;
                    }

                    if (typeof(Def).IsAssignableFrom(field.FieldType) &&
                        field.GetValue(target) is Def def &&
                        def.modContentPack != null)
                    {
                        return def.modContentPack.PackageId;
                    }
                }
            }
            catch (Exception)
            {
            }

            return assembly;
        }

        private static string GetTypeOwner(Type type)
        {
            return type.Assembly == typeof(Def).Assembly
                ? "ludeon.rimworld"
                : type.Assembly.GetName().Name ?? "global";
        }

        private static TimeSpan ToTimeSpan(long stopwatchTicks)
        {
            return TimeSpan.FromSeconds(
                Math.Max(0L, stopwatchTicks) / (double)Stopwatch.Frequency);
        }

        private readonly struct DeferredWorkItem
        {
            internal DeferredWorkItem(
                string key,
                string name,
                Action execute,
                long enqueuedAt)
            {
                Key = key;
                Name = name;
                Execute = execute;
                EnqueuedAt = enqueuedAt;
            }

            internal string Key { get; }

            internal string Name { get; }

            internal Action Execute { get; }

            internal long EnqueuedAt { get; }
        }

        private readonly struct DeferredWorkDescriptor
        {
            internal DeferredWorkDescriptor(string owner, string name)
            {
                Owner = owner;
                Name = name;
            }

            internal string Owner { get; }

            internal string Name { get; }
        }
    }

    internal sealed class DeferredWorkSnapshot
    {
        internal DeferredWorkSnapshot(
            IReadOnlyList<DeferredWorkMeasurement> measurements)
        {
            Measurements = new List<DeferredWorkMeasurement>(
                measurements ??
                throw new ArgumentNullException(nameof(measurements)))
                .AsReadOnly();
        }

        internal IReadOnlyList<DeferredWorkMeasurement> Measurements { get; }
    }

    internal sealed class DeferredWorkMeasurement
    {
        internal DeferredWorkMeasurement(
            string owner,
            string name,
            long calls,
            long failures,
            TimeSpan totalTime,
            TimeSpan maximumTime,
            TimeSpan averageWaitTime,
            TimeSpan maximumWaitTime)
        {
            Owner = owner;
            Name = name;
            Calls = calls;
            Failures = failures;
            TotalTime = totalTime;
            MaximumTime = maximumTime;
            AverageWaitTime = averageWaitTime;
            MaximumWaitTime = maximumWaitTime;
        }

        internal string Owner { get; }

        internal string Name { get; }

        internal long Calls { get; }

        internal long Failures { get; }

        internal TimeSpan TotalTime { get; }

        internal TimeSpan MaximumTime { get; }

        internal TimeSpan AverageWaitTime { get; }

        internal TimeSpan MaximumWaitTime { get; }
    }
}
