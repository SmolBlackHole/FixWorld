using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using FixWorld.Diagnostics;
using HarmonyLib;
using Verse;

namespace FixWorld.Loading
{
    internal static class StagedLoadingRunner
    {
        private const int UiRefreshMilliseconds = 150;
        private static readonly long UiRefreshTicks =
            Math.Max(1L, Stopwatch.Frequency * UiRefreshMilliseconds / 1000L);
        private static readonly FieldInfo PendingActionsField = RequireField(
            "toExecuteWhenFinished");
        private static readonly FieldInfo ExecutingField = RequireField(
            "executingToExecuteWhenFinished");
        private static readonly FieldInfo EventQueueField = RequireField("eventQueue");

        private static bool scheduled;
        private static bool running;
        private static bool frameBoundaryRequested;

        internal static bool IsRunning => running;

        internal static bool ShouldRunOriginal()
        {
            if (!LoadingSession.IsActive || !UnityData.IsInMainThread)
            {
                return true;
            }

            if (running)
            {
                return true;
            }

            if (scheduled)
            {
                return false;
            }

            List<Action> actions = GetPendingActions();
            if (actions.Count == 0)
            {
                return true;
            }

            try
            {
                scheduled = true;
                PrependLongEvent(Run(actions));
                Log.Message(
                    "[FixWorld] Scheduled " + actions.Count +
                    " delayed initialization tasks as a staged long event.");
                return false;
            }
            catch (Exception exception)
            {
                scheduled = false;
                Log.Error(
                    "[FixWorld] Could not schedule staged initialization; " +
                    "falling back to RimWorld: " + exception);
                return true;
            }
        }

        internal static bool ConsumeFrameBoundaryRequest()
        {
            if (!running)
            {
                frameBoundaryRequested = false;
                return false;
            }

            bool requested = frameBoundaryRequested;
            frameBoundaryRequested = false;
            return requested;
        }

        private static IEnumerable Run(List<Action> actions)
        {
            scheduled = false;
            running = true;
            ExecutingField.SetValue(null, true);
            bool outerProfilerStarted = false;
            try
            {
                if (actions.Count > 0)
                {
                    DeepProfiler.Start("ExecuteToExecuteWhenFinished()");
                    outerProfilerStarted = true;
                }

                FrameScheduler frameScheduler = new FrameScheduler();
                int stagedContentMods = 0;
                int stagedFinalizations = 0;
                for (int index = 0; index < actions.Count; index++)
                {
                    Action action = actions[index];
                    string label = GetActionLabel(action);
                    LoadingSession.ReportDelayedInitialization(
                        label,
                        index + 1,
                        actions.Count);

                    if (RequestFrameIfDue(frameScheduler))
                    {
                        yield return null;
                    }

                    if (FinalizationPipeline.TryCreate(
                            action,
                            out FinalizationPipeline finalizationPipeline))
                    {
                        stagedFinalizations++;
                        foreach (object ignored in RunFinalizationAction(
                                     action,
                                     label,
                                     finalizationPipeline,
                                     frameScheduler))
                        {
                            yield return ignored;
                        }
                    }
                    else if (ContentLoadingPipeline.TryCreate(
                                 action,
                                 out ContentLoadingPipeline contentPipeline))
                    {
                        stagedContentMods++;
                        foreach (object ignored in RunContentAction(
                                     action,
                                     label,
                                     contentPipeline,
                                     frameScheduler))
                        {
                            yield return ignored;
                        }
                    }
                    else
                    {
                        RunRegularAction(action, label);
                        yield return null;
                    }
                }

                if (stagedContentMods > 0)
                {
                    Log.Message(
                        "[FixWorld] Staged content loading completed for " +
                        stagedContentMods + " mods.");
                }

                if (stagedFinalizations > 0)
                {
                    Log.Message(
                        "[FixWorld] Staged static initialization completed for " +
                        stagedFinalizations + " finalization action.");
                }
            }
            finally
            {
                try
                {
                    if (outerProfilerStarted)
                    {
                        DeepProfiler.End();
                    }
                }
                finally
                {
                    actions.Clear();
                    ExecutingField.SetValue(null, false);
                    frameBoundaryRequested = false;
                    running = false;
                    scheduled = false;
                }
            }

            LoaderCompletion.Complete("staged-runner");
        }

        private static void PrependLongEvent(IEnumerable action)
        {
            object queue = EventQueueField.GetValue(null) ??
                           throw new InvalidOperationException(
                               "RimWorld long-event queue is unavailable.");
            MethodInfo clear = queue.GetType().GetMethod("Clear") ??
                               throw new MissingMethodException(
                                   queue.GetType().FullName,
                                   "Clear");
            MethodInfo enqueue = queue.GetType().GetMethod("Enqueue") ??
                                 throw new MissingMethodException(
                                     queue.GetType().FullName,
                                     "Enqueue");
            List<object> queuedEvents = ((IEnumerable)queue).Cast<object>().ToList();

            clear.Invoke(queue, null);
            try
            {
                LongEventHandler.QueueLongEvent(
                    action,
                    null,
                    OnRunnerException,
                    showExtraUIInfo: false,
                    forceHideUI: false);
                foreach (object queuedEvent in queuedEvents)
                {
                    enqueue.Invoke(queue, new[] { queuedEvent });
                }
            }
            catch
            {
                clear.Invoke(queue, null);
                foreach (object queuedEvent in queuedEvents)
                {
                    enqueue.Invoke(queue, new[] { queuedEvent });
                }

                throw;
            }
        }

        private static void OnRunnerException(Exception exception)
        {
            Log.Error("[FixWorld] Staged initialization failed: " + exception);
        }

        private static List<Action> GetPendingActions()
        {
            return PendingActionsField.GetValue(null) as List<Action> ??
                   throw new InvalidOperationException(
                       "RimWorld delayed initialization queue is unavailable.");
        }

        private static string GetActionLabel(Action action)
        {
            MethodInfo method = action.Method;
            string declaringType = method.DeclaringType?.ToString() ?? "<dynamic>";
            return declaringType + " -> " + method;
        }

        private static void RunRegularAction(Action action, string label)
        {
            DeepProfiler.Start(label);
            long actionStartedAt = Stopwatch.GetTimestamp();
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Log.Error(
                    "Could not execute post-long-event action. Exception: " + exception);
            }
            finally
            {
                BenchmarkRecorder.ObserveDelayedAction(
                    action,
                    label,
                    Stopwatch.GetTimestamp() - actionStartedAt);
                DeepProfiler.End();
            }
        }

        private static IEnumerable RunContentAction(
            Action originalAction,
            string label,
            ContentLoadingPipeline pipeline,
            FrameScheduler frameScheduler)
        {
            long executionTicks = 0L;
            DeepProfiler.Start(label);
            try
            {
                for (int index = 0; index < pipeline.Steps.Count; index++)
                {
                    ContentLoadingStep step = pipeline.Steps[index];
                    LoadingSession.ReportContentLoading(
                        pipeline.Mod.Name,
                        step.DisplayName,
                        index + 1,
                        pipeline.Steps.Count);
                    if (RequestFrameIfDue(frameScheduler))
                    {
                        yield return null;
                    }

                    bool succeeded = true;
                    DeepProfiler.Start(step.ProfilerLabel);
                    long stepStartedAt = Stopwatch.GetTimestamp();
                    try
                    {
                        step.Execute();
                    }
                    catch (Exception exception)
                    {
                        succeeded = false;
                        Log.Error(
                            "Could not execute post-long-event action. Exception: " +
                            exception);
                    }
                    finally
                    {
                        executionTicks += Stopwatch.GetTimestamp() - stepStartedAt;
                        DeepProfiler.End();
                    }

                    if (!succeeded)
                    {
                        yield break;
                    }

                    yield return null;
                }
            }
            finally
            {
                BenchmarkRecorder.ObserveDelayedAction(
                    originalAction,
                    label,
                    executionTicks);
                DeepProfiler.End();
            }
        }

        private static IEnumerable RunFinalizationAction(
            Action originalAction,
            string label,
            FinalizationPipeline pipeline,
            FrameScheduler frameScheduler)
        {
            long executionTicks = 0L;
            DeepProfiler.Start(label);
            try
            {
                bool staticInitializationSucceeded = true;
                DeepProfiler.Start("StaticConstructorOnStartupUtility.CallAll()");
                try
                {
                    for (int index = 0; index < pipeline.Constructors.Count; index++)
                    {
                        StaticConstructorTarget target = pipeline.Constructors[index];
                        LoadingSession.ReportStaticConstructor(
                            target.Type.FullName ?? target.Type.Name,
                            target.ModName,
                            index + 1,
                            pipeline.Constructors.Count);
                        if (RequestFrameIfDue(frameScheduler))
                        {
                            yield return null;
                        }

                        bool succeeded = true;
                        long constructorStartedAt = Stopwatch.GetTimestamp();
                        try
                        {
                            FinalizationPipeline.RunConstructor(target);
                        }
                        catch (Exception exception)
                        {
                            succeeded = false;
                            Log.Error(
                                "Error in static constructor of " + target.Type + ": " +
                                exception);
                        }
                        finally
                        {
                            long elapsedTicks =
                                Stopwatch.GetTimestamp() - constructorStartedAt;
                            executionTicks += elapsedTicks;
                            BenchmarkRecorder.ObserveStaticConstructor(
                                target,
                                elapsedTicks,
                                succeeded);
                        }

                        yield return null;
                    }

                    LoadingSession.ReportFinalization(
                        "Finalizing mod frameworks",
                        pipeline.CallAllPostfixOwners == null
                            ? "Completing RimWorld static initialization"
                            : "Harmony postfixes: " + pipeline.CallAllPostfixOwners);
                    yield return null;

                    FinalizationStepResult finishResult = RunFinalizationStep(
                        FinalizationPipeline.CompleteStaticInitialization,
                        "Finalize static initialization");
                    executionTicks += finishResult.ElapsedTicks;
                    BenchmarkRecorder.ObserveStaticConstructorTail(
                        finishResult.ElapsedTicks);
                    staticInitializationSucceeded = finishResult.Succeeded;

                    if (staticInitializationSucceeded &&
                        pipeline.ShouldCheckMissingAttributes)
                    {
                        LoadingSession.ReportFinalization(
                            "Checking startup attributes",
                            "Developer-mode validation");
                        yield return null;

                        FinalizationStepResult attributeCheckResult =
                            RunFinalizationStep(
                                FinalizationPipeline.CheckMissingAttributes,
                                "Check static constructor attributes");
                        executionTicks += attributeCheckResult.ElapsedTicks;
                        staticInitializationSucceeded =
                            attributeCheckResult.Succeeded;
                    }
                }
                finally
                {
                    DeepProfiler.End();
                }

                if (!staticInitializationSucceeded)
                {
                    yield break;
                }

                FinalizationStepResult floatMenuResult = RunFinalizationStep(
                    FinalizationPipeline.InitializeFloatMenus,
                    null);
                executionTicks += floatMenuResult.ElapsedTicks;
                if (!floatMenuResult.Succeeded)
                {
                    yield break;
                }

                if (RequestFrameIfDue(frameScheduler))
                {
                    yield return null;
                }

                FinalizationStepResult atlasResult = RunFinalizationStep(
                    FinalizationPipeline.BakeAtlases,
                    "Atlas baking.");
                executionTicks += atlasResult.ElapsedTicks;
                if (!atlasResult.Succeeded)
                {
                    yield break;
                }

                yield return null;
                FinalizationStepResult cleanupResult = RunFinalizationStep(
                    FinalizationPipeline.CleanUp,
                    "Garbage Collection");
                executionTicks += cleanupResult.ElapsedTicks;
                if (!cleanupResult.Succeeded)
                {
                    yield break;
                }

                yield return null;
            }
            finally
            {
                BenchmarkRecorder.ObserveDelayedAction(
                    originalAction,
                    label,
                    executionTicks);
                DeepProfiler.End();
            }
        }

        private static FinalizationStepResult RunFinalizationStep(
            Action action,
            string profilerLabel)
        {
            if (profilerLabel != null)
            {
                DeepProfiler.Start(profilerLabel);
            }

            long startedAt = Stopwatch.GetTimestamp();
            try
            {
                action();
                return new FinalizationStepResult(
                    true,
                    Stopwatch.GetTimestamp() - startedAt);
            }
            catch (Exception exception)
            {
                Log.Error(
                    "Could not execute post-long-event action. Exception: " + exception);
                return new FinalizationStepResult(
                    false,
                    Stopwatch.GetTimestamp() - startedAt);
            }
            finally
            {
                if (profilerLabel != null)
                {
                    DeepProfiler.End();
                }
            }
        }

        private static bool RequestFrameIfDue(FrameScheduler scheduler)
        {
            if (!scheduler.IsDue())
            {
                return false;
            }

            frameBoundaryRequested = true;
            return true;
        }

        private static FieldInfo RequireField(string name)
        {
            return AccessTools.Field(typeof(LongEventHandler), name) ??
                   throw new MissingFieldException(typeof(LongEventHandler).FullName, name);
        }

        private sealed class FrameScheduler
        {
            private long nextRefreshAt;

            internal bool IsDue()
            {
                long now = Stopwatch.GetTimestamp();
                if (now < nextRefreshAt)
                {
                    return false;
                }

                nextRefreshAt = now + UiRefreshTicks;
                return true;
            }
        }

        private readonly struct FinalizationStepResult
        {
            internal readonly bool Succeeded;
            internal readonly long ElapsedTicks;

            internal FinalizationStepResult(bool succeeded, long elapsedTicks)
            {
                Succeeded = succeeded;
                ElapsedTicks = elapsedTicks;
            }
        }
    }
}
