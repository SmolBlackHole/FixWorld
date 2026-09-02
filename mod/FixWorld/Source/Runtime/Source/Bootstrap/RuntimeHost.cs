using System;
using System.IO;
using FixWorld.Diagnostics;
using FixWorld.Integration;
using FixWorld.Lifecycle;
using FixWorld.Loading;
using FixWorld.Preloader;
using FixWorld.Scheduling;
using FixWorld.Textures;
using Verse;

namespace FixWorld.Runtime
{
    internal static class RuntimeHost
    {
        private static readonly object Sync = new object();

        private static IDisposable lifecycleSubscription;
        private static bool earlyReady;
        private static bool modBootReady;

        internal static void StartEarly()
        {
            lock (Sync)
            {
                if (earlyReady)
                {
                    return;
                }

                RemoveLegacyModAssembly();
                PreloaderTimelineSnapshot timeline =
                    PreloaderTimelineState.Capture();
                FixWorldScheduler.Initialize(ReportMainThreadError);
                FixWorldEvents.Initialize();
                try
                {
                    if (!RimWorldHooks.InstallPlayData())
                    {
                        throw new InvalidOperationException(
                            "FixWorld.Runtime could not install its play-data hook.");
                    }
                }
                catch
                {
                    RimWorldHooks.Uninstall();
                    FixWorldScheduler.Shutdown();
                    FixWorldEvents.Shutdown();
                    throw;
                }

                earlyReady = true;
                Log.Message(
                    "[FixWorld.Runtime] Early infrastructure ready; workers=" +
                    FixWorldScheduler.WorkerCount + ".");
                Log.Message(
                    "[FixWorld.Runtime] Early timeline; " +
                    PreloaderTimelineState.Format(timeline) + ".");
            }
        }

        internal static bool BeginModBoot()
        {
            lock (Sync)
            {
                if (modBootReady)
                {
                    return true;
                }

                if (!earlyReady)
                {
                    throw new InvalidOperationException(
                        "FixWorld.Runtime is not early-ready.");
                }

                try
                {
                    LoadingSession.Start();
                    LoadingTelemetry.Start(BenchmarkRecorder.Enabled);
                    lifecycleSubscription =
                        FixWorldEvents.Subscribe<RimWorldLifecycleEvent>(
                            ConsumeLifecycleEvent);
                    if (!RimWorldHooks.InstallRuntime(
                            BenchmarkRecorder.Enabled))
                    {
                        throw new InvalidOperationException(
                            "FixWorld.Runtime could not install its runtime hooks.");
                    }
                }
                catch (Exception exception)
                {
                    lifecycleSubscription?.Dispose();
                    lifecycleSubscription = null;
                    RimWorldHooks.Uninstall();
                    FixWorldScheduler.Shutdown();
                    FixWorldEvents.Shutdown();
                    FixWorldRuntime.Fail(exception);
                    Log.Error(
                        "[FixWorld.Runtime] Mod-boot initialization failed; " +
                        "RimWorld will use its original loader: " + exception);
                    return false;
                }

                modBootReady = true;
                Log.Message(
                    "[FixWorld.Runtime] Runtime hooks installed before mod boot; " +
                    "benchmark=" + BenchmarkRecorder.Enabled + ".");
                return true;
            }
        }

        internal static void AttachMod(
            RuntimeModAttachmentSnapshot attachment)
        {
            if (attachment == null)
            {
                throw new ArgumentNullException(nameof(attachment));
            }

            TextureDdsCache.Initialize(
                attachment.Content.RootDir,
                attachment.Settings.DdsCacheMaxGiB);
            Log.Message(
                "[FixWorld.Runtime] Normal mod attached; assembly=FixWorld.Mod, " +
                "hooks=True, workers=" +
                FixWorldScheduler.WorkerCount + ".");
        }

        internal static void Shutdown()
        {
            try
            {
                RimWorldLifecycle.NotifyShuttingDown();
                FixWorldEvents.Pump();
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FixWorld.Runtime] Could not publish shutdown lifecycle: " +
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
                    "[FixWorld.Runtime] Scheduler shutdown failed: " + exception);
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
                        "[FixWorld.Runtime] DDS shutdown failed: " + exception);
                }
            }
            else
            {
                Log.Warning(
                    "[FixWorld.Runtime] Scheduler workers did not stop within " +
                    "two seconds; DDS resources remain open until process exit.");
            }

            try
            {
                FixWorldEvents.Shutdown();
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FixWorld.Runtime] Event bus shutdown failed: " + exception);
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
                "[FixWorld.Runtime] Main-thread action failed (" + name +
                "): " + exception);
        }

        private static void RemoveLegacyModAssembly()
        {
            string runtimeDirectory = Path.GetDirectoryName(
                typeof(RuntimeHost).Assembly.Location);
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                throw new InvalidOperationException(
                    "FixWorld.Runtime has no assembly location.");
            }

            string modRoot = Path.GetFullPath(
                Path.Combine(runtimeDirectory, "..", ".."));
            string assembliesDirectory = Path.Combine(modRoot, "Assemblies");
            string currentAssembly = Path.Combine(
                assembliesDirectory,
                "FixWorld.Mod.dll");
            if (!File.Exists(currentAssembly))
            {
                return;
            }

            string legacyAssembly = Path.Combine(
                assembliesDirectory,
                "FixWorld.dll");
            string legacySymbols = Path.Combine(
                assembliesDirectory,
                "FixWorld.pdb");
            bool removed = false;
            if (File.Exists(legacyAssembly))
            {
                File.Delete(legacyAssembly);
                removed = true;
            }

            if (File.Exists(legacySymbols))
            {
                File.Delete(legacySymbols);
                removed = true;
            }

            if (removed)
            {
                Log.Message(
                    "[FixWorld.Runtime] Removed the superseded FixWorld.dll.");
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
