using System;
using System.Diagnostics;
using System.IO;

namespace FixWorld.Preloader
{
    internal static class PreloaderHost
    {
        internal static void Start()
        {
            PreloaderLog log = null;
            try
            {
                string gameRoot = FindGameRoot();
                log = new PreloaderLog(Path.Combine(gameRoot, "FixWorld.Preloader.log"));
                Environment.SetEnvironmentVariable(
                    "FIXWORLD_PRELOADER_ACTIVE",
                    "1",
                    EnvironmentVariableTarget.Process);
                log.Write(
                    "Doorstop entered FixWorld preloader. The main FixWorld assembly " +
                    "remains deferred to RimWorld's mod loader.");
            }
            catch (Exception exception)
            {
                Environment.SetEnvironmentVariable(
                    "FIXWORLD_PRELOADER_ACTIVE",
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
