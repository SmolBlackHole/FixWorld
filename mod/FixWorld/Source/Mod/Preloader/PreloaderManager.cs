using System;
using System.IO;
using UnityEngine;

namespace FixWorld.Preloader
{
    internal static class PreloaderManager
    {
        private static readonly object Sync = new object();
        private static PreloaderInstallationPaths paths;

        internal static void Configure(string modRoot)
        {
            lock (Sync)
            {
                DirectoryInfo dataDirectory = new DirectoryInfo(Application.dataPath);
                string gameRoot = dataDirectory.Parent?.FullName;
                if (string.IsNullOrEmpty(gameRoot))
                {
                    throw new DirectoryNotFoundException(
                        "FixWorld could not locate the RimWorld game directory.");
                }

                paths = new PreloaderInstallationPaths(
                    gameRoot,
                    Path.Combine(
                        modRoot,
                        "Tools",
                        "Windows-x64",
                        "Doorstop-4.4.0",
                        "winhttp.dll"),
                    Path.Combine(
                        modRoot,
                        "Tools",
                        "Windows-x64",
                        "FixWorld.Preloader.dll"));
            }
        }

        internal static PreloaderState GetState()
        {
            lock (Sync)
            {
                return paths == null
                    ? new PreloaderState(
                        PreloaderStatus.Unavailable,
                        "The FixWorld early loader is not configured.",
                        false)
                    : PreloaderInstallation.GetState(paths);
            }
        }

        internal static PreloaderState Install()
        {
            lock (Sync)
            {
                return PreloaderInstallation.Install(RequirePaths());
            }
        }

        private static PreloaderInstallationPaths RequirePaths()
        {
            return paths ?? throw new InvalidOperationException(
                "The FixWorld early loader is not configured.");
        }
    }
}
