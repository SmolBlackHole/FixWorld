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
                FixWorldScheduler.Initialize();
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
                    FixWorldScheduler.Shutdown();
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
                    lifecycleSubscription?.Dispose();
                    lifecycleSubscription = null;
                    break;
            }
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
