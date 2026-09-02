using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Verse;

namespace FixWorld.PlayData
{
    internal sealed class DeferredWorkQueue
    {
        private static readonly long FrameBudgetTicks =
            Stopwatch.Frequency / 5;

        private readonly object sync = new object();
        private readonly List<DeferredWorkItem> pending =
            new List<DeferredWorkItem>();
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

                pending.Add(new DeferredWorkItem(GetLabel(action), action));
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
                pending.Add(new DeferredWorkItem(
                    "Load all bios",
                    RimWorldPlayData.LoadBios));
                pending.Add(new DeferredWorkItem(
                    "Inject selected language data",
                    RimWorldPlayData.InjectLanguage));
                pending.Add(new DeferredWorkItem(
                    "Finalize play data",
                    RimWorldPlayData.FinalizeRuntime));
                pending.Add(new DeferredWorkItem(
                    "Reset message count",
                    Log.ResetMessageCount));
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

        private static IEnumerable Run(
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
                    for (int index = 0; index < work.Count; index++)
                    {
                        DeferredWorkItem item = work[index];
                        operation.Report(item.Name, index, work.Count);
                        LongEventHandler.SetCurrentEventText(
                            "FixWorld: " + item.Name);
                        DeepProfiler.Start(item.Name);
                        try
                        {
                            item.Execute();
                        }
                        catch (Exception exception)
                        {
                            Log.Error(
                                "Could not execute post-long-event action. " +
                                "Exception: " + exception);
                        }
                        finally
                        {
                            DeepProfiler.End();
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

        private readonly struct DeferredWorkItem
        {
            internal DeferredWorkItem(string name, Action execute)
            {
                Name = name;
                Execute = execute;
            }

            internal string Name { get; }

            internal Action Execute { get; }
        }
    }
}
