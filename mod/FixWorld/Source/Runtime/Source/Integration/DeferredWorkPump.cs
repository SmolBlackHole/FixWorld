using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FixWorld.PlayData;
using FixWorld.Runtime;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class DeferredWorkPump
    {
        private static readonly FieldInfo EventEnumeratorField =
            RequireEventEnumeratorField();
        private static readonly FieldInfo ExecutingField = RequireField(
            "executingToExecuteWhenFinished");
        private static readonly FieldInfo WorkField = RequireField(
            "toExecuteWhenFinished");

        internal static bool TryBegin(object queuedEvent)
        {
            if (queuedEvent == null ||
                (bool)ExecutingField.GetValue(null))
            {
                return false;
            }

            List<Action> work =
                (List<Action>)WorkField.GetValue(null);
            if (work.Count == 0 ||
                !RuntimeHost.TransitionStage(
                    PlayDataLoadStage.DeferredMainThreadWork))
            {
                return false;
            }

            ExecutingField.SetValue(null, true);
            EventEnumeratorField.SetValue(
                queuedEvent,
                new DeferredWorkEnumerator(work));
            return true;
        }

        private static FieldInfo RequireEventEnumeratorField()
        {
            FieldInfo currentEvent = RequireField("currentEvent");
            return AccessTools.Field(
                       currentEvent.FieldType,
                       "eventActionEnumerator") ??
                   throw new MissingFieldException(
                       currentEvent.FieldType.FullName,
                       "eventActionEnumerator");
        }

        private static FieldInfo RequireField(string name)
        {
            return AccessTools.Field(typeof(LongEventHandler), name) ??
                   throw new MissingFieldException(
                       typeof(LongEventHandler).FullName,
                       name);
        }

        private sealed class DeferredWorkEnumerator : IEnumerator
        {
            private readonly List<Action> work;
            private int index;
            private bool finished;
            private bool profiling;
            private bool started;

            internal DeferredWorkEnumerator(List<Action> work)
            {
                this.work = work;
            }

            public object Current => null;

            public bool MoveNext()
            {
                if (finished)
                {
                    return false;
                }

                if (!started)
                {
                    started = true;
                    profiling = DeepProfiler.enabled && Prefs.LogVerbose;
                    if (profiling)
                    {
                        DeepProfiler.Start(
                            "ExecuteToExecuteWhenFinished()");
                    }
                }

                if (index >= work.Count)
                {
                    Finish();
                    return false;
                }

                Action action = work[index++];
                if (profiling)
                {
                    DeepProfiler.Start(
                        action.Method.DeclaringType + " -> " + action.Method);
                }

                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Log.Error(
                        "Could not execute post-long-event action. " +
                        "Exception: " + exception);
                }
                finally
                {
                    if (profiling)
                    {
                        DeepProfiler.End();
                    }
                }

                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            private void Finish()
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                work.Clear();
                ExecutingField.SetValue(null, false);
                if (profiling)
                {
                    DeepProfiler.End();
                }

                RuntimeHost.CompletePlayData();
            }
        }
    }
}
