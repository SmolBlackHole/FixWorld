using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FixWorld.Textures;
using Verse;

namespace FixWorld.Loading
{
    internal sealed class LoadingCoordinator
    {
        private readonly LoadingStageExecutor executor = new LoadingStageExecutor();

        internal IEnumerable Run(IReadOnlyList<Action> actions)
        {
            int preparedContentThrough = -1;
            bool outerProfilerStarted = false;
            executor.BeginRun();
            try
            {
                if (actions.Count > 0)
                {
                    DeepProfiler.Start("ExecuteToExecuteWhenFinished()");
                    outerProfilerStarted = true;
                }

                for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                {
                    if (actionIndex > preparedContentThrough &&
                        TryCollectContentBatch(
                            actions,
                            actionIndex,
                            out List<ModContentPack> contentMods,
                            out preparedContentThrough) &&
                        TextureDdsCache.TryCreateValidationPlan(
                            contentMods,
                            out LoadingActionPlan validationPlan))
                    {
                        foreach (object frame in executor.RunPlan(
                                     validationPlan,
                                     actionIndex + 1,
                                     actions.Count))
                        {
                            yield return frame;
                        }
                    }

                    Action action = actions[actionIndex];
                    string label = GetActionLabel(action);
                    LoadingActionPlan plan =
                        VanillaLoadingActionAdapter.CreatePlan(action, label);
                    foreach (object frame in executor.RunPlan(
                                 plan,
                                 actionIndex + 1,
                                 actions.Count))
                    {
                        yield return frame;
                    }
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
                    executor.EndRun();
                }
            }
        }

        private static string GetActionLabel(Action action)
        {
            MethodInfo method = action.Method;
            string declaringType = method.DeclaringType?.ToString() ?? "<dynamic>";
            return declaringType + " -> " + method;
        }

        private static bool TryCollectContentBatch(
            IReadOnlyList<Action> actions,
            int startIndex,
            out List<ModContentPack> mods,
            out int lastIndex)
        {
            mods = new List<ModContentPack>();
            lastIndex = startIndex;
            for (int index = startIndex; index < actions.Count; index++)
            {
                if (!ContentLoadingPipeline.TryCreateCompatible(
                        actions[index],
                        out ContentLoadingPipeline content))
                {
                    break;
                }

                mods.Add(content.Mod);
                lastIndex = index;
            }

            return mods.Count > 0;
        }
    }
}
