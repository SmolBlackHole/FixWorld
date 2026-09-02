using System;
using System.IO;
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

                RemoveLegacyModAssembly();
                PreloaderTimelineSnapshot timeline =
                    PreloaderTimelineState.Capture();
                RuntimeContext created = new RuntimeContext();
                try
                {
                    Volatile.Write(ref current, created);
                    if (!RimWorldHooks.InstallBootstrap(
                            BenchmarkRecorder.Enabled))
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
            RuntimeContext stopped;
            lock (Sync)
            {
                stopped = current;
                current = null;
            }

            RimWorldHooks.Uninstall();
            stopped?.Dispose();
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
    }
}
