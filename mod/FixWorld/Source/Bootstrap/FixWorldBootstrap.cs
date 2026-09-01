using System;
using FixWorld.Diagnostics;
using FixWorld.Integration;
using FixWorld.Lifecycle;
using FixWorld.Loading;
using FixWorld.Preloader;
using FixWorld.Runtime;
using FixWorld.Scheduling;
using FixWorld.Textures;
using Verse;

namespace FixWorld
{
    internal static class FixWorldBootstrap
    {
        private static readonly object Sync = new object();
        private static IDisposable lifecycleSubscription;
        private static bool initialized;
        private static bool shuttingDown;

        internal static void Initialize(
            FixWorldMod owner,
            ModContentPack content,
            FixWorldSettings settings)
        {
            lock (Sync)
            {
                if (initialized)
                {
                    return;
                }

                PreloaderTimelineSnapshot preloaderTimeline =
                    PreloaderTimelineState.Capture();
                FixWorldScheduler.Initialize(ReportMainThreadError);
                FixWorldEvents.Initialize();
                try
                {
                    if (!RimWorldHooks.Install(BenchmarkRecorder.Enabled))
                    {
                        throw new InvalidOperationException(
                            "FixWorld could not install its required RimWorld hooks.");
                    }

                    LoadingSession.Start(true);
                    LoadingTelemetry.Start(BenchmarkRecorder.Enabled);
                    TextureDdsCache.Initialize(content.RootDir, settings);
                    PreloaderManager.Configure(content.RootDir);
                    PreloaderPrompt.Configure(owner, settings);
                    lifecycleSubscription =
                        FixWorldEvents.Subscribe<RimWorldLifecycleEvent>(
                            ConsumeLifecycleEvent);
                }
                catch
                {
                    lifecycleSubscription?.Dispose();
                    lifecycleSubscription = null;
                    RimWorldHooks.Uninstall();
                    if (FixWorldScheduler.Shutdown())
                    {
                        TextureDdsCache.Shutdown();
                    }

                    FixWorldEvents.Shutdown();
                    throw;
                }

                initialized = true;
                Log.Message(
                    "[FixWorld] Initialized; hooks=True" +
                    ", benchmark=" + BenchmarkRecorder.Enabled +
                    ", workers=" + FixWorldScheduler.WorkerCount +
                    ", earlyLoader=" + preloaderTimeline.Active + ".");
                Log.Message(
                    "[FixWorld] Early timeline; " +
                    PreloaderTimelineState.Format(preloaderTimeline) + ".");
            }
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                if (!initialized || shuttingDown)
                {
                    return;
                }

                shuttingDown = true;
            }

            try
            {
                RimWorldLifecycle.NotifyShuttingDown();
                FixWorldEvents.Pump();
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FixWorld] Could not publish shutdown lifecycle: " +
                    exception);
            }

            lifecycleSubscription?.Dispose();
            lifecycleSubscription = null;

            bool workersStopped;
            try
            {
                workersStopped = FixWorldScheduler.Shutdown();
            }
            catch (Exception exception)
            {
                workersStopped = false;
                Log.Error(
                    "[FixWorld] Scheduler shutdown failed: " + exception);
            }

            if (workersStopped)
            {
                try
                {
                    TextureDdsCache.Shutdown();
                }
                catch (Exception exception)
                {
                    Log.Error(
                        "[FixWorld] DDS shutdown failed: " + exception);
                }
            }
            else
            {
                Log.Warning(
                    "[FixWorld] Scheduler workers did not stop within two seconds; " +
                    "DDS resources remain open until process exit.");
            }

            try
            {
                FixWorldEvents.Shutdown();
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FixWorld] Event bus shutdown failed: " + exception);
            }

            RimWorldHooks.Uninstall();
        }

        private static void ConsumeLifecycleEvent(
            RimWorldLifecycleEvent lifecycleEvent)
        {
            switch (lifecycleEvent.Kind)
            {
                case RimWorldLifecycleEventKind.PlayDataReady:
                    TextureDdsCache.Complete();
                    break;
                case RimWorldLifecycleEventKind.MainMenuReady:
                    if (CompleteStartup(lifecycleEvent.Source))
                    {
                        Log.Message("[FixWorld] Main menu ready.");
                    }

                    TextureDdsCache.StartDeferredBuild();
                    PreloaderPrompt.TryShow();
                    break;
                case RimWorldLifecycleEventKind.GameReady:
                    CompleteStartup(lifecycleEvent.Source);
                    TextureDdsCache.StartDeferredBuild();
                    Log.Message(
                        "[FixWorld] Game ready; generation=" +
                        lifecycleEvent.GameGeneration + ".");
                    break;
                case RimWorldLifecycleEventKind.ShuttingDown:
                    break;
            }
        }

        private static void ReportMainThreadError(
            string name,
            Exception exception)
        {
            Log.Error(
                "[FixWorld] Main-thread action failed (" + name + "): " +
                exception);
        }

        private static bool CompleteStartup(string source)
        {
            if (!LoadingSession.TryComplete())
            {
                return false;
            }

            LoadingTelemetry.Complete();
            BenchmarkRecorder.Complete(source);
            return true;
        }
    }
}
