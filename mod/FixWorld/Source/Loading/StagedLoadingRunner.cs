using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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

                long nextUiRefreshAt = 0L;
                for (int index = 0; index < actions.Count; index++)
                {
                    Action action = actions[index];
                    string label = GetActionLabel(action);
                    LoadingSession.ReportDelayedInitialization(
                        label,
                        index + 1,
                        actions.Count);

                    long now = Stopwatch.GetTimestamp();
                    if (now >= nextUiRefreshAt)
                    {
                        nextUiRefreshAt = now + UiRefreshTicks;
                        frameBoundaryRequested = true;
                        yield return null;
                    }

                    DeepProfiler.Start(label);
                    try
                    {
                        action();
                    }
                    catch (Exception exception)
                    {
                        Log.Error(
                            "Could not execute post-long-event action. Exception: " +
                            exception);
                    }
                    finally
                    {
                        DeepProfiler.End();
                    }

                    yield return null;
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

        private static FieldInfo RequireField(string name)
        {
            return AccessTools.Field(typeof(LongEventHandler), name) ??
                   throw new MissingFieldException(typeof(LongEventHandler).FullName, name);
        }
    }
}
