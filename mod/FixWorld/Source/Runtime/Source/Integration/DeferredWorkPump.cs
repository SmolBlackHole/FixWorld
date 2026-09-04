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
        private static volatile bool isolateLoadingFrame;

        private static readonly FieldInfo EventEnumeratorField =
            RequireEventEnumeratorField();
        private static readonly FieldInfo ExecutingField = RequireField(
            "executingToExecuteWhenFinished");
        private static readonly FieldInfo WorkField = RequireField(
            "toExecuteWhenFinished");

        internal static bool RequiresIsolatedLoadingFrame =>
            isolateLoadingFrame;

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

            RuntimeHost.BeginTextureDiscovery();
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
            private int contentBarrier = -1;
            private int index;
            private int knownWorkCount;
            private bool finished;
            private bool profiling;
            private bool started;

            internal DeferredWorkEnumerator(List<Action> work)
            {
                this.work = work;
                RefreshContentBarrier();
                UpdateLoadingFrameIsolation();
            }

            public object Current => null;

            public bool MoveNext()
            {
                if (finished)
                {
                    return false;
                }

                BeginProfiling();
                if (index >= work.Count)
                {
                    Finish();
                    return false;
                }

                RefreshContentBarrier();
                ExecuteNext();
                RefreshContentBarrier();
                UpdateLoadingFrameIsolation();

                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            private void BeginProfiling()
            {
                if (started)
                {
                    return;
                }

                started = true;
                profiling = DeepProfiler.enabled && Prefs.LogVerbose;
                if (profiling)
                {
                    DeepProfiler.Start(
                        "ExecuteToExecuteWhenFinished()");
                }
            }

            private void RefreshContentBarrier()
            {
                int count = work.Count;
                for (int candidate = knownWorkCount;
                     candidate < count;
                     candidate++)
                {
                    if (IsContentReload(work[candidate]))
                    {
                        contentBarrier = candidate;
                    }
                }

                knownWorkCount = count;
            }

            private void UpdateLoadingFrameIsolation()
            {
                isolateLoadingFrame = index <= contentBarrier;
            }

            private static bool IsContentReload(Action action)
            {
                MethodInfo method = action?.Method;
                Type declaringType = method?.DeclaringType;
                return declaringType != null &&
                       declaringType.DeclaringType == typeof(ModContentPack) &&
                       method.Name.IndexOf(
                           "<ReloadContent>",
                           StringComparison.Ordinal) >= 0;
            }

            private void ExecuteNext()
            {
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
            }

            private void Finish()
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                isolateLoadingFrame = false;
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
