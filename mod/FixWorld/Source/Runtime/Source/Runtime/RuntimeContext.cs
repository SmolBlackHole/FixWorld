using System;
using FixWorld.Diagnostics;
using FixWorld.Events;
using FixWorld.Lifecycle;
using FixWorld.PlayData;
using FixWorld.Scheduling;
using FixWorld.Textures;
using Verse;

namespace FixWorld.Runtime
{
    internal sealed class RuntimeContext : IDisposable
    {
        private const string FixWorldPackageId = "smolblackhole.fixworld";
        private const float DefaultDdsCacheMaxGiB = 6.0f;
        private const int MaximumLifecycleEventsPerPump = 64;

        private readonly object attachmentSync = new object();
        private readonly EventBus events;
        private readonly MainThreadQueue mainThread;
        private readonly JobScheduler scheduler;
        private readonly IDisposable lifecycleSubscription;
        private readonly RuntimeTelemetryStore telemetry;
        private object attachedMod;
        private string diagnosticsText =
            "No completed startup diagnostics are available yet.";
        private bool disposed;

        internal RuntimeContext()
        {
            events = new EventBus();
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
            telemetry = new RuntimeTelemetryStore();
            Lifecycle = new RimWorldLifecycle(events);
            Textures = new TextureDdsCache(scheduler, mainThread);
            lifecycleSubscription = events.Subscribe<RimWorldLifecycleEvent>(
                ConsumeLifecycleEvent);
        }

        internal RimWorldLifecycle Lifecycle { get; }

        internal TextureDdsCache Textures { get; }

        internal int WorkerCount => scheduler.WorkerCount;

        internal string DiagnosticsText => diagnosticsText;

        internal string ClearDdsCache()
        {
            return Textures.ClearCache();
        }

        internal string RetryFailedDdsBuilds()
        {
            return Textures.RetryFailedBuilds();
        }

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

        internal void BeginPlayData()
        {
            if (!telemetry.Start())
            {
                return;
            }

            Textures.BeginIndex();
        }

        internal bool TransitionStage(PlayDataLoadStage stage)
        {
            return telemetry.Transition(stage);
        }

        internal void BeginTextureDiscovery()
        {
            Textures.BeginTextureDiscovery();
        }

        internal void PrepareTextures()
        {
            telemetry.Transition(PlayDataLoadStage.InitializeTextureCache);
            ModContentPack fixWorld = null;
            try
            {
                foreach (ModContentPack mod in
                         LoadedModManager.RunningModsListForReading)
                {
                    if (string.Equals(
                            mod.PackageId,
                            FixWorldPackageId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        fixWorld = mod;
                        break;
                    }
                }

                if (fixWorld == null)
                {
                    Log.Warning(
                        "[FixWorld] DDS cache was not prepared because the " +
                        "FixWorld content pack is not active.");
                }
                else
                {
                    Textures.Attach(fixWorld.RootDir, DefaultDdsCacheMaxGiB);
                }
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[FixWorld] DDS cache initialization failed; RimWorld will " +
                    "load source textures normally: " + exception);
            }

            telemetry.Transition(PlayDataLoadStage.IndexTextureSources);
            try
            {
                Textures.Prepare();
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[FixWorld] DDS texture indexing failed; RimWorld will " +
                    "load source textures normally: " + exception);
            }
        }

        internal void CompletePlayData()
        {
            if (!telemetry.CompletePlayData())
            {
                return;
            }

            Textures.CompleteLoading();
            Lifecycle.NotifyPlayDataReady("rimworld-play-data");
        }

        internal void FailPlayData(Exception exception)
        {
            if (!telemetry.Abort())
            {
                return;
            }

            Textures.CompleteLoading();
            Log.Error("[FixWorld.Runtime] Play-data load failed: " + exception);
        }

        internal void Pump()
        {
            mainThread.BindCurrentThread();
            mainThread.Pump(64, TimeSpan.FromMilliseconds(4));
            Lifecycle.ObserveFrame();
            events.Pump();
        }

        internal bool TryGetLoadingSnapshot(
            out PlayDataLoadingSnapshot snapshot)
        {
            return telemetry.TryGetLoadingSnapshot(out snapshot);
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

        private void ConsumeLifecycleEvent(
            RimWorldLifecycleEvent lifecycleEvent)
        {
            switch (lifecycleEvent.Kind)
            {
                case RimWorldLifecycleEventKind.MainMenuReady:
                    CompleteStartup(lifecycleEvent.Source);
                    Textures.StartBackgroundBuild();
                    break;
                case RimWorldLifecycleEventKind.GameReady:
                    CompleteStartup(lifecycleEvent.Source);
                    Textures.StartBackgroundBuild();
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
            RuntimeDiagnosticsSnapshot diagnostics = telemetry.Complete(
                source,
                Textures.GetSnapshot(),
                new RuntimeSchedulerSnapshot(
                    scheduler.WorkerCount,
                    mainThread.PendingCount),
                SystemMemoryMetrics.Read());
            if (diagnostics == null)
            {
                return false;
            }

            diagnosticsText = RuntimeDiagnosticsSummary.FormatDetails(diagnostics);
            Log.Message(RuntimeDiagnosticsSummary.Format(diagnostics));
            BenchmarkExporter.Write(diagnostics);
            return true;
        }
    }
}
