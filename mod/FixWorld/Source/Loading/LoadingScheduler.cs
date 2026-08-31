using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace FixWorld.Loading
{
    internal sealed class LoadingScheduler
    {
        private const int UiRefreshMilliseconds = 150;
        private static readonly long UiRefreshTicks =
            Math.Max(1L, Stopwatch.Frequency * UiRefreshMilliseconds / 1000L);
        private static readonly int WorkerCount =
            Math.Max(1, Environment.ProcessorCount - 1);

        private static bool running;
        private static bool frameBoundaryRequested;

        private long nextRefreshAt;
        private long currentPlanExecutionTicks;
        private bool currentStageSucceeded;

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

        internal void BeginRun()
        {
            nextRefreshAt = 0L;
            frameBoundaryRequested = false;
            running = true;
        }

        internal void EndRun()
        {
            frameBoundaryRequested = false;
            running = false;
        }

        internal IEnumerable RunPlan(
            LoadingActionPlan plan,
            int currentAction,
            int totalActions)
        {
            currentPlanExecutionTicks = 0L;
            DeepProfiler.Start(plan.Label);
            try
            {
                if (plan.StageCount == 1)
                {
                    LoadingPipelineStage stage = plan.GetStage(0);
                    if (stage.Dependencies.Count != 0)
                    {
                        throw new InvalidOperationException(
                            "A single loading stage cannot have dependencies: " +
                            plan.Label);
                    }

                    ValidateStage(stage);
                    foreach (object frame in RunStage(stage, currentAction, totalActions))
                    {
                        yield return frame;
                    }

                    yield break;
                }

                int[] executionOrder = BuildExecutionOrder(plan);
                for (int index = 0; index < executionOrder.Length; index++)
                {
                    LoadingPipelineStage stage = plan.GetStage(executionOrder[index]);
                    foreach (object frame in RunStage(stage, currentAction, totalActions))
                    {
                        yield return frame;
                    }

                    if (!currentStageSucceeded)
                    {
                        yield break;
                    }
                }
            }
            finally
            {
                LoadingTelemetry.ObserveDelayedAction(plan, currentPlanExecutionTicks);
                DeepProfiler.End();
            }
        }

        private IEnumerable RunStage(
            LoadingPipelineStage stage,
            int currentAction,
            int totalActions)
        {
            currentStageSucceeded = true;
            IEnumerable execution =
                stage.ExecutionMode == LoadingExecutionMode.Parallel ||
                stage.ExecutionMode == LoadingExecutionMode.ParallelThenCommit
                    ? RunParallelStage(stage, currentAction, totalActions)
                    : RunSequentialStage(stage, currentAction, totalActions);
            foreach (object frame in execution)
            {
                yield return frame;
            }
        }

        private IEnumerable RunSequentialStage(
            LoadingPipelineStage stage,
            int currentAction,
            int totalActions)
        {
            LoadingTelemetry.ReportStage(stage, 0, stage.TaskCount);
            for (int taskIndex = 0; taskIndex < stage.TaskCount; taskIndex++)
            {
                LoadingWorkItem item = stage.GetTask(taskIndex);
                LoadingTelemetry.ReportWork(item, currentAction, totalActions);
                if (RequestFrameIfDue())
                {
                    yield return null;
                }

                WorkExecution result = ExecuteOnMainThread(item);
                currentPlanExecutionTicks += result.ExecutionTicks;
                LoadingTelemetry.ObserveWork(
                    item,
                    result.ExecutionTicks,
                    result.ExecutionTicks,
                    0L,
                    0L,
                    result.ExecutionTicks,
                    result.Succeeded);
                LoadingTelemetry.ReportStage(stage, taskIndex + 1, stage.TaskCount);

                if (!result.Succeeded && !item.ContinueOnFailure)
                {
                    currentStageSucceeded = false;
                    yield break;
                }

                yield return null;
            }
        }

        private IEnumerable RunParallelStage(
            LoadingPipelineStage stage,
            int currentAction,
            int totalActions)
        {
            LoadingTelemetry.ReportStage(stage, 0, stage.TaskCount);
            ParallelWorkResult[] results = new ParallelWorkResult[stage.TaskCount];
            long queuedAt = Stopwatch.GetTimestamp();
            int nextTask = -1;
            int completedTasks = 0;
            int workers = Math.Min(WorkerCount, stage.TaskCount);
            Task[] workerTasks = new Task[workers];

            for (int worker = 0; worker < workers; worker++)
            {
                workerTasks[worker] = Task.Run(() =>
                {
                    while (true)
                    {
                        int taskIndex = Interlocked.Increment(ref nextTask);
                        if (taskIndex >= stage.TaskCount)
                        {
                            return;
                        }

                        LoadingWorkItem item = stage.GetTask(taskIndex);
                        results[taskIndex] = ExecuteOnWorker(
                            stage.ExecutionMode,
                            item,
                            queuedAt);
                        Interlocked.Increment(ref completedTasks);
                    }
                });
            }

            Task barrier = Task.WhenAll(workerTasks);
            while (!barrier.IsCompleted)
            {
                LoadingTelemetry.ReportStage(
                    stage,
                    Volatile.Read(ref completedTasks),
                    stage.TaskCount);
                RequestFrame();
                yield return null;
            }

            barrier.GetAwaiter().GetResult();
            for (int taskIndex = 0; taskIndex < stage.TaskCount; taskIndex++)
            {
                LoadingWorkItem item = stage.GetTask(taskIndex);
                ParallelWorkResult result = results[taskIndex];
                LoadingTelemetry.ReportWork(item, currentAction, totalActions);

                if (result.Exception != null)
                {
                    LogFailure(item, result.Exception);
                }

                if (stage.ExecutionMode == LoadingExecutionMode.ParallelThenCommit &&
                    result.Succeeded)
                {
                    if (RequestFrameIfDue())
                    {
                        yield return null;
                    }

                    long commitStartedAt = Stopwatch.GetTimestamp();
                    try
                    {
                        result.Prepared.Commit();
                    }
                    catch (Exception exception)
                    {
                        result.Succeeded = false;
                        LogFailure(item, exception);
                    }
                    finally
                    {
                        result.MainThreadTicks +=
                            Stopwatch.GetTimestamp() - commitStartedAt;
                        result.WallTicks = Stopwatch.GetTimestamp() - queuedAt;
                    }
                }

                long executionTicks = result.WorkerThreadTicks + result.MainThreadTicks;
                currentPlanExecutionTicks += executionTicks;
                LoadingTelemetry.ObserveWork(
                    item,
                    executionTicks,
                    result.MainThreadTicks,
                    result.WorkerThreadTicks,
                    result.WaitTicks,
                    result.WallTicks,
                    result.Succeeded);
                LoadingTelemetry.ReportStage(stage, taskIndex + 1, stage.TaskCount);

                if (!result.Succeeded && !item.ContinueOnFailure)
                {
                    currentStageSucceeded = false;
                }
            }
        }

        private static WorkExecution ExecuteOnMainThread(LoadingWorkItem item)
        {
            if (item.Execute == null)
            {
                throw new InvalidOperationException(
                    "A main-thread loading task has no execution delegate: " +
                    item.Subject);
            }

            if (item.ProfilerLabel != null)
            {
                DeepProfiler.Start(item.ProfilerLabel);
            }

            long startedAt = Stopwatch.GetTimestamp();
            try
            {
                item.Execute();
                return new WorkExecution(
                    true,
                    Stopwatch.GetTimestamp() - startedAt);
            }
            catch (Exception exception)
            {
                LogFailure(item, exception);
                return new WorkExecution(
                    false,
                    Stopwatch.GetTimestamp() - startedAt);
            }
            finally
            {
                if (item.ProfilerLabel != null)
                {
                    DeepProfiler.End();
                }
            }
        }

        private static ParallelWorkResult ExecuteOnWorker(
            LoadingExecutionMode mode,
            LoadingWorkItem item,
            long queuedAt)
        {
            long startedAt = Stopwatch.GetTimestamp();
            ParallelWorkResult result = new ParallelWorkResult
            {
                Succeeded = true,
                WaitTicks = startedAt - queuedAt
            };
            try
            {
                if (mode == LoadingExecutionMode.ParallelThenCommit)
                {
                    result.Prepared = item.Prepare();
                }
                else
                {
                    item.Execute();
                }
            }
            catch (Exception exception)
            {
                result.Succeeded = false;
                result.Exception = exception;
            }
            finally
            {
                long completedAt = Stopwatch.GetTimestamp();
                result.WorkerThreadTicks = completedAt - startedAt;
                result.WallTicks = completedAt - queuedAt;
            }

            return result;
        }

        private bool RequestFrameIfDue()
        {
            long startedAt = Stopwatch.GetTimestamp();
            try
            {
                long now = Stopwatch.GetTimestamp();
                if (now < nextRefreshAt)
                {
                    return false;
                }

                nextRefreshAt = now + UiRefreshTicks;
                RequestFrame();
                return true;
            }
            finally
            {
                LoadingTelemetry.ObserveOverhead(
                    LoadingOverheadKind.Scheduling,
                    Stopwatch.GetTimestamp() - startedAt);
            }
        }

        private static void RequestFrame()
        {
            frameBoundaryRequested = true;
        }

        private static int[] BuildExecutionOrder(LoadingActionPlan plan)
        {
            Dictionary<int, int> stageIndices = new Dictionary<int, int>(plan.StageCount);
            for (int stageIndex = 0; stageIndex < plan.StageCount; stageIndex++)
            {
                LoadingPipelineStage stage = plan.GetStage(stageIndex);
                if (stageIndices.ContainsKey(stage.Id))
                {
                    throw new InvalidOperationException(
                        "Duplicate loading stage id " + stage.Id + " in " + plan.Label);
                }

                stageIndices.Add(stage.Id, stageIndex);
                ValidateStage(stage);
            }

            for (int stageIndex = 0; stageIndex < plan.StageCount; stageIndex++)
            {
                LoadingPipelineStage stage = plan.GetStage(stageIndex);
                foreach (int dependency in stage.Dependencies)
                {
                    if (!stageIndices.ContainsKey(dependency))
                    {
                        throw new InvalidOperationException(
                            "Loading stage " + stage.Id + " depends on unavailable stage " +
                            dependency + " in " + plan.Label);
                    }
                }
            }

            int[] executionOrder = new int[plan.StageCount];
            bool[] scheduled = new bool[plan.StageCount];
            for (int orderIndex = 0; orderIndex < executionOrder.Length; orderIndex++)
            {
                int readyStageIndex = -1;
                for (int stageIndex = 0; stageIndex < plan.StageCount; stageIndex++)
                {
                    if (scheduled[stageIndex])
                    {
                        continue;
                    }

                    LoadingPipelineStage stage = plan.GetStage(stageIndex);
                    bool ready = true;
                    foreach (int dependency in stage.Dependencies)
                    {
                        if (!scheduled[stageIndices[dependency]])
                        {
                            ready = false;
                            break;
                        }
                    }

                    if (ready)
                    {
                        readyStageIndex = stageIndex;
                        break;
                    }
                }

                if (readyStageIndex < 0)
                {
                    throw new InvalidOperationException(
                        "Loading stage graph contains a dependency cycle in " + plan.Label);
                }

                scheduled[readyStageIndex] = true;
                executionOrder[orderIndex] = readyStageIndex;
            }

            return executionOrder;
        }

        private static void ValidateStage(LoadingPipelineStage stage)
        {
            for (int taskIndex = 0; taskIndex < stage.TaskCount; taskIndex++)
            {
                LoadingWorkItem item = stage.GetTask(taskIndex);
                bool parallel =
                    stage.ExecutionMode == LoadingExecutionMode.Parallel ||
                    stage.ExecutionMode == LoadingExecutionMode.ParallelThenCommit;
                if (parallel && item.Affinity != LoadingThreadAffinity.WorkerSafe)
                {
                    throw new InvalidOperationException(
                        "Parallel loading stage contains a main-thread task: " +
                        item.Subject);
                }

                if (stage.ExecutionMode == LoadingExecutionMode.ParallelThenCommit &&
                    item.Prepare == null)
                {
                    throw new InvalidOperationException(
                        "ParallelThenCommit task has no preparation delegate: " +
                        item.Subject);
                }

                if (stage.ExecutionMode != LoadingExecutionMode.ParallelThenCommit &&
                    item.Execute == null)
                {
                    throw new InvalidOperationException(
                        "Loading task has no execution delegate: " + item.Subject);
                }
            }
        }

        private static void LogFailure(LoadingWorkItem item, Exception exception)
        {
            if (item.Operation == LoadingStep.RunStaticConstructors)
            {
                Log.Error(
                    "Error in static constructor of " + item.Subject + ": " + exception);
                return;
            }

            Log.Error("Could not execute loading task " + item.Subject + ": " + exception);
        }

        private readonly struct WorkExecution
        {
            internal readonly bool Succeeded;
            internal readonly long ExecutionTicks;

            internal WorkExecution(bool succeeded, long executionTicks)
            {
                Succeeded = succeeded;
                ExecutionTicks = executionTicks;
            }
        }

        private struct ParallelWorkResult
        {
            internal bool Succeeded;
            internal Exception Exception;
            internal PreparedLoadingWork Prepared;
            internal long MainThreadTicks;
            internal long WorkerThreadTicks;
            internal long WaitTicks;
            internal long WallTicks;
        }
    }
}
