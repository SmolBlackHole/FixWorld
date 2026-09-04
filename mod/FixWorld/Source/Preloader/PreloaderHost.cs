using System;
using System.Diagnostics;
using System.IO;

namespace FixWorld.Preloader
{
    internal static class PreloaderHost
    {
        internal static void Start(long entryTimestamp)
        {
            PreloaderLog log = null;
            try
            {
                string gameRoot = PreloaderPaths.FindGameRoot();
                log = new PreloaderLog(Path.Combine(gameRoot, "FixWorld.Preloader.log"));
                string saveDataFolder = PreloaderPaths.FindSaveDataFolder();
                if (!ActiveModConfig.IsFixWorldActive(saveDataFolder))
                {
                    log.Write(
                        "FixWorld is not active in ModsConfig.xml; the early " +
                        "loader is disabled for this launch.");
                    return;
                }

                PreloaderTimelineCapture.Start(entryTimestamp);
                EarlyLoaderBridge.Start(log);
                log.Write(
                    "Doorstop entered FixWorld preloader, armed FixWorld.Loader, " +
                    "and started the early assembly timeline.");
            }
            catch (Exception exception)
            {
                Environment.SetEnvironmentVariable(
                    PreloaderTimelineContract.ActiveVariable,
                    null,
                    EnvironmentVariableTarget.Process);
                string message = "Preloader disabled for this launch: " + exception;
                if (log == null)
                {
                    Console.WriteLine("[FixWorld.Preloader] " + message);
                }
                else
                {
                    log.Write(message);
                }

                return;
            }

            StartJob(
                log,
                "bounded DDS read-ahead",
                () =>
                {
                    DdsReadAhead.Start();
                    return true;
                });
        }

        private static void StartJob(
            PreloaderLog log,
            string name,
            Func<bool> start)
        {
            try
            {
                if (start())
                {
                    log.Write("Queued " + name + ".");
                }
            }
            catch (Exception exception)
            {
                log.Write(
                    name + " was not started, but the early loader remains " +
                    "active: " + exception);
            }
        }
    }
}
