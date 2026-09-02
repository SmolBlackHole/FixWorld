using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FixWorld.Content;

namespace FixWorld.Preloader
{
    internal static class CombinedXmlPreload
    {
        internal static bool Start(PreloaderLog log)
        {
            if (!CombinedXmlCacheContract.Enabled)
            {
                return false;
            }

            Thread thread = new Thread(() => Run(log))
            {
                IsBackground = true,
                Name = "FixWorld combined XML preload",
                Priority = ThreadPriority.BelowNormal
            };
            thread.Start();
            return true;
        }

        private static void Run(PreloaderLog log)
        {
            try
            {
                string path = CombinedXmlCacheContract.GetPath(
                    PreloaderPaths.FindSaveDataFolder());
                if (!File.Exists(path))
                {
                    log.Write("Combined XML cache candidate is not present.");
                    return;
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                CombinedXmlArtifact artifact = CombinedXmlCacheContract.Read(path);
                stopwatch.Stop();
                if (artifact == null || CombinedXmlCacheContract.IsStopRequested())
                {
                    return;
                }

                CombinedXmlCacheContract.Publish(
                    artifact,
                    stopwatch.Elapsed.TotalMilliseconds);
                log.Write(
                    "Combined XML cache candidate parsed in " +
                    stopwatch.Elapsed.TotalMilliseconds.ToString("F1") + " ms.");
            }
            catch (Exception exception)
            {
                log.Write(
                    "Combined XML cache preload failed; RimWorld will use its " +
                    "normal XML path: " + exception);
            }
        }
    }
}
