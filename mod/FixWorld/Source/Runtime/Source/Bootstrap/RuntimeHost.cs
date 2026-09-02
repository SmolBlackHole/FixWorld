using System;
using System.Threading;
using FixWorld.Diagnostics;
using FixWorld.Integration;
using FixWorld.PlayData;
using FixWorld.Preloader;
using Verse;

namespace FixWorld.Runtime
{
    internal static class RuntimeHost
    {
        private static readonly object Sync = new object();
        private static RuntimeContext current;
        private static bool shutdown;

        internal static RuntimeContext Current =>
            Volatile.Read(ref current) ??
            throw new InvalidOperationException(
                "FixWorld.Runtime has not been started.");

        internal static void StartEarly()
        {
            lock (Sync)
            {
                if (current != null)
                {
                    return;
                }

                if (shutdown)
                {
                    throw new InvalidOperationException(
                        "FixWorld.Runtime has already shut down.");
                }

                PreloaderTimelineSnapshot timeline =
                    PreloaderTimelineState.Capture();
                RuntimeContext created = new RuntimeContext();
                try
                {
                    Volatile.Write(ref current, created);
                    if (!RimWorldHooks.InstallBootstrap(
                            BenchmarkExporter.Enabled))
                    {
                        throw new InvalidOperationException(
                            "FixWorld.Runtime could not install its hooks.");
                    }
                }
                catch
                {
                    Volatile.Write(ref current, null);
                    RimWorldHooks.Uninstall();
                    created.Dispose();
                    throw;
                }

                Log.Message(
                    "[FixWorld.Runtime] Runtime context ready; workers=" +
                    created.WorkerCount + ".");
                Log.Message(
                    "[FixWorld.Runtime] Early timeline; " +
                    PreloaderTimelineState.Format(timeline) + ".");
            }
        }

        internal static void AttachMod(
            RuntimeModAttachmentSnapshot attachment)
        {
            Current.AttachMod(attachment);
            Log.Message(
                "[FixWorld.Runtime] Normal mod attached; assembly=FixWorld.Mod, " +
                "workers=" + Current.WorkerCount + ".");
        }

        internal static void RunPlayData()
        {
            Current.PlayData.Load();
        }

        internal static bool ActivateRuntimeHooks()
        {
            return Volatile.Read(ref current) != null &&
                   RimWorldHooks.InstallRuntime();
        }

        internal static bool TryCaptureDeferred(Action action)
        {
            RuntimeContext context = Volatile.Read(ref current);
            return context != null && context.DeferredWork.TryCapture(action);
        }

        internal static bool TryGetLoadingSnapshot(
            out PlayDataLoadingSnapshot snapshot)
        {
            RuntimeContext context = Volatile.Read(ref current);
            if (context == null)
            {
                snapshot = default;
                return false;
            }

            return context.Loading.TryGetSnapshot(out snapshot);
        }

        internal static string GetDiagnosticsText()
        {
            RuntimeContext context = Volatile.Read(ref current);
            return context == null
                ? "FixWorld.Runtime is not active."
                : context.DiagnosticsText;
        }

        internal static void Pump()
        {
            Volatile.Read(ref current)?.Pump();
        }

        internal static void NotifyMainMenuReady()
        {
            Volatile.Read(ref current)?.Lifecycle.NotifyMainMenuReady();
        }

        internal static void NotifyGameEnded(Game game)
        {
            Volatile.Read(ref current)?.Lifecycle.NotifyGameEnded(game);
        }

        internal static void Shutdown()
        {
            RuntimeContext stoppedContext;
            lock (Sync)
            {
                if (shutdown)
                {
                    return;
                }

                shutdown = true;
                stoppedContext = current;
                current = null;
            }

            RimWorldHooks.Uninstall();
            stoppedContext?.Dispose();
        }
    }
}
