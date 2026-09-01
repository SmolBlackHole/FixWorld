using System;
using FixWorld.Diagnostics;
using FixWorld.Integration;
using FixWorld.Loading;
using FixWorld.Preloader;
using FixWorld.Scheduling;
using FixWorld.Textures;
using Verse;

namespace FixWorld
{
    internal static class FixWorldBootstrap
    {
        private static readonly object Sync = new object();
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
                LoadingSession.Start(true);
                LoadingTelemetry.Start(BenchmarkRecorder.Enabled);
                bool hooksInstalled = RimWorldHooks.Install(BenchmarkRecorder.Enabled);
                TextureDdsCache.Initialize(content.RootDir, settings);
                PreloaderManager.Configure(content.RootDir);
                PreloaderPrompt.Configure(owner, settings);

                initialized = true;
                Log.Message(
                    "[FixWorld] Initialized; hooks=" + hooksInstalled +
                    ", benchmark=" + BenchmarkRecorder.Enabled +
                    ", workers=" + FixWorldScheduler.WorkerCount +
                    ", earlyLoader=" + preloaderTimeline.Active + ".");
                Log.Message(
                    "[FixWorld] Early timeline; " +
                    PreloaderTimelineState.Format(preloaderTimeline) + ".");
            }
        }
    }
}
