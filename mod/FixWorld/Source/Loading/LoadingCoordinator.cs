using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace FixWorld.Loading
{
    internal sealed class LoadingCoordinator
    {
        private readonly LoadingScheduler scheduler = new LoadingScheduler();

        internal IEnumerable Run(IReadOnlyList<Action> actions)
        {
            int stagedContentActions = 0;
            int stagedFinalizationActions = 0;
            bool outerProfilerStarted = false;
            scheduler.BeginRun();
            try
            {
                if (actions.Count > 0)
                {
                    DeepProfiler.Start("ExecuteToExecuteWhenFinished()");
                    outerProfilerStarted = true;
                }

                for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                {
                    Action action = actions[actionIndex];
                    string label = GetActionLabel(action);
                    LoadingActionPlan plan = LoadingActionAdapter.CreatePlan(action, label);
                    if (ContainsOperation(plan, LoadingStep.LoadAudio))
                    {
                        stagedContentActions++;
                    }

                    if (ContainsOperation(plan, LoadingStep.RunStaticConstructors))
                    {
                        stagedFinalizationActions++;
                    }

                    foreach (object frame in scheduler.RunPlan(
                                 plan,
                                 actionIndex + 1,
                                 actions.Count))
                    {
                        yield return frame;
                    }
                }

                if (stagedContentActions > 0)
                {
                    Log.Message(
                        "[FixWorld] Staged content loading completed for " +
                        stagedContentActions + " mods.");
                }

                if (stagedFinalizationActions > 0)
                {
                    Log.Message(
                        "[FixWorld] Staged static initialization completed for " +
                        stagedFinalizationActions + " finalization action.");
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
                    scheduler.EndRun();
                }
            }
        }

        private static bool ContainsOperation(
            LoadingActionPlan plan,
            LoadingStep operation)
        {
            for (int stageIndex = 0; stageIndex < plan.StageCount; stageIndex++)
            {
                if (plan.GetStage(stageIndex).Operation == operation)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetActionLabel(Action action)
        {
            MethodInfo method = action.Method;
            string declaringType = method.DeclaringType?.ToString() ?? "<dynamic>";
            return declaringType + " -> " + method;
        }
    }
}
