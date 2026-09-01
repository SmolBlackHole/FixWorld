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
                string gameRoot = FindGameRoot();
                log = new PreloaderLog(Path.Combine(gameRoot, "FixWorld.Preloader.log"));
                DdsReadAhead.Start();
                log.Write(
                    "Doorstop entered FixWorld preloader, started the early assembly " +
                    "timeline, and queued bounded DDS read-ahead. Runtime hooks remain " +
                    "deferred to RimWorld's mod loader.");
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
            }
        }

        private static string FindGameRoot()
        {
            string executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new InvalidOperationException("Could not locate the RimWorld executable.");
            }

            return Path.GetDirectoryName(executablePath) ??
                   throw new InvalidOperationException("Could not locate the RimWorld directory.");
        }

    }
}
