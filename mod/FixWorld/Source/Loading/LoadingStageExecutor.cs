using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using FixWorld.Scheduling;
using Verse;

namespace FixWorld.Loading
{
    internal sealed class LoadingStageExecutor
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
                for (int stageIndex = 0; stageIndex < plan.StageCount; stageIndex++)
                {
                    LoadingPipelineStage stage = plan.GetStage(stageIndex);
                    ValidateStage(stage);
                    foreach (object frame in RunStage(
                                 stage,
                                 stageIndex,
                                 currentAction,
                                 totalActions))
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
            int stageIndex,
            int currentAction,
            int totalActions)
        {
            currentStageSucceeded = true;
            IEnumerable execution = stage.ExecutionMode ==
                                    LoadingExecutionMode.ParallelThenCommit
                ? RunParallelStage(
                    stage,
                    stageIndex,
                    currentAction,
                    totalActions)
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
            int stageIndex,
            int currentAction,
            int totalActions)
        {
            LoadingEvents.ReportStage(stage, 0, stage.TaskCount);
            long queuedAt = Stopwatch.GetTimestamp();
            int workerLimit = stage.MaxParallelism > 0
                ? stage.MaxParallelism
                : FixWorldScheduler.WorkerCount;
            string concurrencyKey =
                "loader/" + runId + "/" + currentPlanId + "/" + stageIndex;
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
                            return scheduledItem.Prepare();
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

                if (result.Succeeded)
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
                if (stage.ExecutionMode == LoadingExecutionMode.ParallelThenCommit &&
                    item.Affinity != LoadingThreadAffinity.WorkerSafe)
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

                if (stage.ExecutionMode == LoadingExecutionMode.MainThread &&
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
