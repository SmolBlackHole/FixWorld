using System;
using FixWorld.Caching;
using FixWorld.Diagnostics;
using FixWorld.Integration;
using FixWorld.Loading;
using FixWorld.Preloader;
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

                LoadingSession.Start(true);
                LoadingTelemetry.Start(BenchmarkRecorder.Enabled);
                bool hooksInstalled = RimWorldHooks.Install(BenchmarkRecorder.Enabled);
                TextureDdsCache.Initialize(content.RootDir, settings);
                PreloaderManager.Configure(content.RootDir);
                PreloaderPrompt.Configure(owner, settings);

                bool earlyLoader = string.Equals(
                    Environment.GetEnvironmentVariable("FIXWORLD_PRELOADER_ACTIVE"),
                    "1",
                    StringComparison.Ordinal);
                initialized = true;
                Log.Message(
                    "[FixWorld] Initialized; hooks=" + hooksInstalled +
                    ", benchmark=" + BenchmarkRecorder.Enabled +
                    ", earlyLoader=" + earlyLoader + ".");
            }
        }
    }
}
