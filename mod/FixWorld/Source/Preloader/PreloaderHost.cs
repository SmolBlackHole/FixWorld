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
                PreloaderTimelineCapture.Start(entryTimestamp);
                string gameRoot = PreloaderPaths.FindGameRoot();
                log = new PreloaderLog(Path.Combine(gameRoot, "FixWorld.Preloader.log"));
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

            try
            {
                if (CombinedXmlPreload.Start(log))
                {
                    log.Write("Queued combined XML cache preload.");
                }

                DdsReadAhead.Start();
                log.Write("Queued bounded DDS read-ahead.");
            }
            catch (Exception exception)
            {
                log.Write(
                    "DDS read-ahead was not started, but the early loader remains " +
                    "active: " + exception);
            }
        }
    }
}
