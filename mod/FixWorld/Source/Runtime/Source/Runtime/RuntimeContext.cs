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

        private readonly IDisposable lifecycleSubscription;
        private bool disposed;

        internal RuntimeContext()
        {
            Events = new EventBus();
            Events.Register<PlayDataLoadStageEvent>(
                MaximumStageEventsPerPump,
                error => Log.Error(
                    "[FixWorld] Play-data event subscriber failed: " + error));
            Events.Register<RimWorldLifecycleEvent>(
                MaximumLifecycleEventsPerPump,
                error => Log.Error(
                    "[FixWorld] Lifecycle event subscriber failed: " + error));

            JobSchedulerOptions options = RuntimeSchedulerSettings.Create();
            Scheduler = new JobScheduler(options);
            MainThread = new MainThreadQueue(
                options.QueueCapacity,
                (name, error) => Log.Error(
                    "[FixWorld] Main-thread action failed (" + name + "): " +
                    error));
            Loading = new PlayDataLoadingState(Events);
            Telemetry = new PlayDataTelemetry(Events);
            Lifecycle = new RimWorldLifecycle(Events);
            Textures = new TextureCacheAdapter(Scheduler, MainThread);

            PlayDataStageRunner stageRunner = new PlayDataStageRunner(Events);
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
            lifecycleSubscription = Events.Subscribe<RimWorldLifecycleEvent>(
                ConsumeLifecycleEvent);
        }

        internal EventBus Events { get; }

        internal JobScheduler Scheduler { get; }

        internal MainThreadQueue MainThread { get; }

        internal RimWorldLifecycle Lifecycle { get; }

        internal PlayDataLoadingState Loading { get; }

        internal PlayDataTelemetry Telemetry { get; }

        internal PlayDataLoadPipeline PlayData { get; }

        internal DeferredWorkQueue DeferredWork { get; }

        internal TextureCacheAdapter Textures { get; }

        internal int WorkerCount => Scheduler.WorkerCount;

        internal void AttachMod(RuntimeModAttachmentSnapshot attachment)
        {
            if (attachment == null)
            {
                throw new ArgumentNullException(nameof(attachment));
            }

            Textures.Attach(
                attachment.Content.RootDir,
                attachment.Settings.DdsCacheMaxGiB);
        }

        internal void Pump()
        {
            MainThread.BindCurrentThread();
            MainThread.Pump(64, TimeSpan.FromMilliseconds(4));
            Lifecycle.ObserveFrame();
            Events.Pump();
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
                Events.Pump();
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FixWorld.Runtime] Could not publish shutdown: " + exception);
            }

            lifecycleSubscription.Dispose();
            Loading.Dispose();
            Telemetry.Dispose();
            Textures.Shutdown();
            if (!Scheduler.Shutdown(TimeSpan.FromSeconds(2)))
            {
                Log.Warning(
                    "[FixWorld.Runtime] Scheduler workers did not stop within " +
                    "two seconds.");
            }

            MainThread.Dispose();
            Events.Dispose();
        }

        private void BeginPlayData()
        {
            Loading.Start();
            Telemetry.Start();
        }

        private void CompletePlayData()
        {
            Textures.Complete();
            Lifecycle.NotifyPlayDataReady("fixworld-play-data-pipeline");
        }

        private void FailPlayData(Exception exception)
        {
            Loading.Abort();
            Telemetry.Abort();
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

            Telemetry.Complete();
            BenchmarkRecorder.Complete(
                source,
                Telemetry.GetMeasurement(),
                XmlLoadingSnapshot.Empty,
                Textures.Snapshot());
            return true;
        }
    }
}
