using System;
using FixWorld.Textures;
using FixWorld.Scheduling;
using FixWorld.Diagnostics;
using FixWorld.Preloader;
using Verse;

namespace FixWorld.Loading
{
    internal static class LoaderCompletion
    {
        private static readonly object Sync = new object();

        private static bool playDataReady;
        private static bool interfaceInitialized;
        private static bool interfaceReady;
        private static string playDataSource;
        private static string initializedInterface;

        internal static void NotifyPlayDataReady(string source)
        {
            lock (Sync)
            {
                if (playDataReady)
                {
                    return;
                }

                playDataReady = true;
                playDataSource = string.IsNullOrWhiteSpace(source)
                    ? "play-data"
                    : source;
            }

            TextureDdsCache.Complete();
            FixWorldScheduler.DrainEvents();
        }

        internal static void NotifyInterfaceInitialized(string interfaceName)
        {
            lock (Sync)
            {
                interfaceInitialized = true;
                initializedInterface = string.IsNullOrWhiteSpace(interfaceName)
                    ? "interface"
                    : interfaceName;
            }
        }

        internal static void TryCompleteInterface()
        {
            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                return;
            }

            string source;
            lock (Sync)
            {
                if (!playDataReady || !interfaceInitialized || interfaceReady)
                {
                    return;
                }

                interfaceReady = true;
                source = playDataSource + "+" + initializedInterface;
            }

            FixWorldScheduler.DrainEvents();
            if (!LoadingSession.TryComplete())
            {
                return;
            }

            LoadingTelemetry.Complete();
            BenchmarkRecorder.Complete(source);
            Log.Message("[FixWorld] Main menu ready.");
            TextureDdsCache.StartDeferredBuild();
            PreloaderPrompt.TryShow();
        }
    }
}
