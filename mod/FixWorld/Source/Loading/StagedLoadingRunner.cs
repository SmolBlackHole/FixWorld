using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace FixWorld.Loading
{
    internal static class StagedLoadingRunner
    {
        private static readonly FieldInfo PendingActionsField = RequireField(
            "toExecuteWhenFinished");
        private static readonly FieldInfo ExecutingField = RequireField(
            "executingToExecuteWhenFinished");
        private static readonly FieldInfo EventQueueField = RequireField("eventQueue");

        private static bool scheduled;
        private static bool running;

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

        private static IEnumerable Run(List<Action> actions)
        {
            scheduled = false;
            running = true;
            ExecutingField.SetValue(null, true);
            try
            {
                LoadingCoordinator coordinator = new LoadingCoordinator();
                foreach (object frame in coordinator.Run(actions))
                {
                    yield return frame;
                }
            }
            finally
            {
                actions.Clear();
                ExecutingField.SetValue(null, false);
                running = false;
                scheduled = false;
            }

            LoaderCompletion.NotifyPlayDataReady("staged-runner");
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

        private static FieldInfo RequireField(string name)
        {
            return AccessTools.Field(typeof(LongEventHandler), name) ??
                   throw new MissingFieldException(typeof(LongEventHandler).FullName, name);
        }
    }
}
