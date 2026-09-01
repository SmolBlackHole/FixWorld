using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FixWorld.Scheduling;
using Verse;

namespace FixWorld.Loading
{
    internal sealed class LoadingScheduler
    {
        private const int UiRefreshMilliseconds = 150;
        private static readonly long UiRefreshTicks =
            Math.Max(1L, Stopwatch.Frequency * UiRefreshMilliseconds / 1000L);
        private static long nextRunId;
        private static bool running;
        private static bool frameBoundaryRequested;

        private long runId;
        private long currentPlanId;
        private long nextRefreshAt;
        private long currentPlanStartedAt;
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
            runId = Interlocked.Increment(ref nextRunId);
            currentPlanId = 0L;
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
            currentPlanId++;
            currentPlanStartedAt = Stopwatch.GetTimestamp();
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
                LoadingTelemetry.ObserveDelayedAction(
                    plan,
                    Stopwatch.GetTimestamp() - currentPlanStartedAt);
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
            LoadingEvents.ReportStage(stage, 0, stage.TaskCount);
            for (int taskIndex = 0; taskIndex < stage.TaskCount; taskIndex++)
            {
                LoadingWorkItem item = stage.GetTask(taskIndex);
                LoadingEvents.ReportWork(item, currentAction, totalActions);
                if (RequestFrameIfDue())
                {
                    yield return null;
                }

                WorkExecution result = ExecuteOnMainThread(item);
                LoadingTelemetry.ObserveWork(
                    item,
                    result.ExecutionTicks,
                    result.ExecutionTicks,
                    0L,
                    0L,
                    result.ExecutionTicks,
                    result.Succeeded);
                LoadingEvents.ReportStage(stage, taskIndex + 1, stage.TaskCount);

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
            LoadingEvents.ReportStage(stage, 0, stage.TaskCount);
            long queuedAt = Stopwatch.GetTimestamp();
            int workerLimit = stage.MaxParallelism > 0
                ? stage.MaxParallelism
                : FixWorldScheduler.WorkerCount;
            string concurrencyKey =
                "loader/" + runId + "/" + currentPlanId + "/" + stage.Id;
            ScheduledJobHandle<PreparedLoadingWork>[] handles =
                new ScheduledJobHandle<PreparedLoadingWork>[stage.TaskCount];

            for (int taskIndex = 0; taskIndex < stage.TaskCount; taskIndex++)
            {
                int scheduledTaskIndex = taskIndex;
                LoadingWorkItem item = stage.GetTask(taskIndex);
                string jobKey = concurrencyKey + "/" + taskIndex;
                handles[taskIndex] = FixWorldScheduler.Schedule(
                    new SchedulerJob<PreparedLoadingWork>(
                        jobKey,
                        item.Subject,
                        SchedulerJobLifetime.Critical,
                        SchedulerJobPriority.High,
                        SchedulerResourceClass.Mixed,
                        cancellationToken =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            LoadingWorkItem scheduledItem =
                                stage.GetTask(scheduledTaskIndex);
                            if (stage.ExecutionMode ==
                                LoadingExecutionMode.ParallelThenCommit)
                            {
                                return scheduledItem.Prepare();
                            }

                            scheduledItem.Execute();
                            return null;
                        },
                        concurrencyKey: concurrencyKey,
                        maxConcurrency: Math.Min(workerLimit, stage.TaskCount)));
            }

            while (true)
            {
                int completedTasks = 0;
                for (int taskIndex = 0; taskIndex < handles.Length; taskIndex++)
                {
                    if (handles[taskIndex].IsTerminal)
                    {
                        completedTasks++;
                    }
                }

                LoadingEvents.ReportStage(
                    stage,
                    completedTasks,
                    stage.TaskCount);
                if (completedTasks == stage.TaskCount)
                {
                    break;
                }

                RequestFrame();
                yield return null;
            }

            for (int taskIndex = 0; taskIndex < stage.TaskCount; taskIndex++)
            {
                LoadingWorkItem item = stage.GetTask(taskIndex);
                ScheduledJobHandle<PreparedLoadingWork> handle = handles[taskIndex];
                ParallelWorkResult result = new ParallelWorkResult
                {
                    Succeeded = handle.State == SchedulerJobState.Completed,
                    Exception = handle.Exception,
                    Prepared = handle.State == SchedulerJobState.Completed
                        ? handle.Result
                        : null,
                    WorkerThreadTicks = handle.ExecutionTicks,
                    WaitTicks = handle.WaitTicks,
                    WallTicks = handle.WallTicks
                };
                LoadingEvents.ReportWork(item, currentAction, totalActions);

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
                        result.Exception = exception;
                        LogFailure(item, exception);
                    }
                    finally
                    {
                        result.MainThreadTicks +=
                            Stopwatch.GetTimestamp() - commitStartedAt;
                    }

                    result.WallTicks = Stopwatch.GetTimestamp() - queuedAt;
                }

                long executionTicks = result.WorkerThreadTicks + result.MainThreadTicks;
                LoadingTelemetry.ObserveWork(
                    item,
                    executionTicks,
                    result.MainThreadTicks,
                    result.WorkerThreadTicks,
                    result.WaitTicks,
                    result.WallTicks,
                    result.Succeeded);
                LoadingEvents.ReportStage(stage, taskIndex + 1, stage.TaskCount);

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
            if (stage.MaxParallelism < 0)
            {
                throw new InvalidOperationException(
                    "Loading stage has an invalid parallelism limit: " + stage.Name);
            }

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
