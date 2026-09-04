using System;
using System.Runtime.CompilerServices;
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
                    if (!RimWorldHooks.InstallBootstrap())
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

        internal static void BeginPlayData() => Current.BeginPlayData();

        internal static bool TransitionStage(PlayDataLoadStage stage)
        {
            RuntimeContext context = Volatile.Read(ref current);
            return context != null && context.TransitionStage(stage);
        }

        internal static void PrepareTextures() => Volatile.Read(ref current)?.PrepareTextures();

        internal static void BeginTextureDiscovery() => Volatile.Read(ref current)?.BeginTextureDiscovery();

        internal static void CompletePlayData() => Volatile.Read(ref current)?.CompletePlayData();

        internal static void FailPlayData(System.Exception exception) => Volatile.Read(ref current)?.FailPlayData(exception);

        internal static bool ActivateRuntimeHooks()
        {
            if (Volatile.Read(ref current) == null ||
                !RimWorldHooks.InstallRuntime())
            {
                return false;
            }

            PreloaderTimelineContract.PublishRuntimeReady();
            return true;
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

            return context.TryGetLoadingSnapshot(out snapshot);
        }

        internal static string GetDiagnosticsText()
        {
            RuntimeContext context = Volatile.Read(ref current);
            return context == null
                ? "FixWorld.Runtime is not active."
                : context.DiagnosticsText;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long StartRuntimeHotpath(RuntimeHotpath hotpath)
        {
            RuntimeContext context = Volatile.Read(ref current);
            return context == null
                ? long.MinValue
                : context.StartRuntimeHotpath(hotpath);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void StopRuntimeHotpath(
            RuntimeHotpath hotpath,
            long startedAt)
        {
            Volatile.Read(ref current)?.StopRuntimeHotpath(
                hotpath,
                startedAt);
        }

        internal static void ObservePathBatch(
            int requests,
            long totalQueueDelayTicks,
            int maximumQueueDelayTicks)
        {
            Volatile.Read(ref current)?.ObservePathBatch(
                requests,
                totalQueueDelayTicks,
                maximumQueueDelayTicks);
        }

        internal static void ObservePathGridJobCreated() => Volatile.Read(ref current)?.ObservePathGridJobCreated();

        internal static void ObservePathDataUpdate(int dirtyCells) =>
            Volatile.Read(ref current)?.ObservePathDataUpdate(dirtyCells);

        internal static void ObserveReachabilityCache(bool hit) => Volatile.Read(ref current)?.ObserveReachabilityCache(hit);

        internal static string ClearDdsCache()
        {
            RuntimeContext context = Volatile.Read(ref current);
            return context == null
                ? "FixWorld.Runtime is not active."
                : context.ClearDdsCache();
        }

        internal static string RetryFailedDdsBuilds()
        {
            RuntimeContext context = Volatile.Read(ref current);
            return context == null
                ? "FixWorld.Runtime is not active."
                : context.RetryFailedDdsBuilds();
        }

        internal static void Pump() => Volatile.Read(ref current)?.Pump();

        internal static void NotifyMainMenuReady() => Volatile.Read(ref current)?.Lifecycle.NotifyMainMenuReady();

        internal static void NotifyGameEnded(Game game) => Volatile.Read(ref current)?.Lifecycle.NotifyGameEnded(game);

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
