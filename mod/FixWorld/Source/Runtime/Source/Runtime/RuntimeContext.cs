using System;
using FixWorld.Diagnostics;
using FixWorld.Events;
using FixWorld.Lifecycle;
using FixWorld.Loading;
using FixWorld.PlayData;
using FixWorld.Scheduling;
using FixWorld.Textures;
using Verse;

namespace FixWorld.Runtime
{
    internal sealed class RuntimeContext : IDisposable
    {
        private const int MaximumStageEventsPerPump = 1024;
        private const int MaximumLifecycleEventsPerPump = 64;

        private readonly object attachmentSync = new object();
        private readonly EventBus events;
        private readonly MainThreadQueue mainThread;
        private readonly JobScheduler scheduler;
        private readonly IDisposable lifecycleSubscription;
        private readonly PlayDataTelemetry telemetry;
        private object attachedMod;
        private bool disposed;

        internal RuntimeContext()
        {
            events = new EventBus();
            events.Register<PlayDataLoadStageEvent>(
                MaximumStageEventsPerPump,
                error => Log.Error(
                    "[FixWorld] Play-data event subscriber failed: " + error));
            events.Register<RimWorldLifecycleEvent>(
                MaximumLifecycleEventsPerPump,
                error => Log.Error(
                    "[FixWorld] Lifecycle event subscriber failed: " + error));

            JobSchedulerOptions options = RuntimeSchedulerSettings.Create();
            scheduler = new JobScheduler(options);
            mainThread = new MainThreadQueue(
                options.QueueCapacity,
                (name, error) => Log.Error(
                    "[FixWorld] Main-thread action failed (" + name + "): " +
                    error));
            Loading = new PlayDataLoadingState(events);
            telemetry = new PlayDataTelemetry(events);
            Lifecycle = new RimWorldLifecycle(events);
            Textures = new TextureCacheAdapter(scheduler, mainThread);

            PlayDataStageRunner stageRunner = new PlayDataStageRunner(events);
            DeferredWorkQueue deferredWork = new DeferredWorkQueue();
            DeferredWork = deferredWork;
            PlayData = new PlayDataLoadPipeline(
                stageRunner,
                new ModLoadingPipeline(),
                new RimWorldPlayData(),
                deferredWork,
                BeginPlayData,
                CompletePlayData,
                FailPlayData);
            lifecycleSubscription = events.Subscribe<RimWorldLifecycleEvent>(
                ConsumeLifecycleEvent);
        }

        internal RimWorldLifecycle Lifecycle { get; }

        internal PlayDataLoadingState Loading { get; }

        internal PlayDataLoadPipeline PlayData { get; }

        internal DeferredWorkQueue DeferredWork { get; }

        internal TextureCacheAdapter Textures { get; }

        internal int WorkerCount => scheduler.WorkerCount;

        internal void AttachMod(RuntimeModAttachmentSnapshot attachment)
        {
            if (attachment == null)
            {
                throw new ArgumentNullException(nameof(attachment));
            }

            lock (attachmentSync)
            {
                if (attachedMod != null)
                {
                    if (ReferenceEquals(attachedMod, attachment.Mod))
                    {
                        return;
                    }

                    throw new InvalidOperationException(
                        "FixWorld.Runtime is already attached to another mod " +
                        "instance.");
                }

                Textures.Attach(
                    attachment.Content.RootDir,
                    attachment.Settings.DdsCacheMaxGiB);
                attachedMod = attachment.Mod;
            }
        }

        internal void Pump()
        {
            mainThread.BindCurrentThread();
            mainThread.Pump(64, TimeSpan.FromMilliseconds(4));
            Lifecycle.ObserveFrame();
            events.Pump();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                Lifecycle.NotifyShuttingDown();
                events.Pump();
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FixWorld.Runtime] Could not publish shutdown: " + exception);
            }

            lifecycleSubscription.Dispose();
            Loading.Dispose();
            telemetry.Dispose();
            Textures.Shutdown();
            if (!scheduler.Shutdown(TimeSpan.FromSeconds(2)))
            {
                Log.Warning(
                    "[FixWorld.Runtime] Scheduler workers did not stop within " +
                    "two seconds.");
            }

            mainThread.Dispose();
            events.Dispose();
        }

        private void BeginPlayData()
        {
            Loading.Start();
            telemetry.Start();
        }

        private void CompletePlayData()
        {
            Textures.Complete();
            Lifecycle.NotifyPlayDataReady("fixworld-play-data-pipeline");
        }

        private void FailPlayData(Exception exception)
        {
            Loading.Abort();
            telemetry.Abort();
            Log.Error("[FixWorld.Runtime] Play-data load failed: " + exception);
        }

        private void ConsumeLifecycleEvent(
            RimWorldLifecycleEvent lifecycleEvent)
        {
            switch (lifecycleEvent.Kind)
            {
                case RimWorldLifecycleEventKind.MainMenuReady:
                    if (CompleteStartup(lifecycleEvent.Source))
                    {
                        Log.Message("[FixWorld] Main menu ready.");
                    }

                    Textures.StartDeferredBuild();
                    break;
                case RimWorldLifecycleEventKind.GameReady:
                    CompleteStartup(lifecycleEvent.Source);
                    Textures.StartDeferredBuild();
                    Log.Message(
                        "[FixWorld] Game ready; generation=" +
                        lifecycleEvent.GameGeneration + ".");
                    break;
                case RimWorldLifecycleEventKind.PlayDataReady:
                case RimWorldLifecycleEventKind.GameEnded:
                case RimWorldLifecycleEventKind.ShuttingDown:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private bool CompleteStartup(string source)
        {
            if (!Loading.Complete())
            {
                return false;
            }

            telemetry.Complete();
            BenchmarkRecorder.Complete(
                source,
                telemetry.GetMeasurement(),
                XmlLoadingSnapshot.Empty,
                Textures.Snapshot());
            return true;
        }
    }
}
