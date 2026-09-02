using System;
using System.IO;
using FixWorld.Migrations;
using UnityEngine;
using Verse;

namespace FixWorld.Preloader
{
    internal sealed class PreloaderService
    {
        private readonly string modRoot;
        private readonly PreloaderInstallationPaths paths;

        internal PreloaderService(string modRoot)
        {
            this.modRoot = Path.GetFullPath(modRoot ??
                throw new ArgumentNullException(nameof(modRoot)));
            DirectoryInfo dataDirectory = new DirectoryInfo(Application.dataPath);
            string gameRoot = dataDirectory.Parent?.FullName;
            if (string.IsNullOrEmpty(gameRoot))
            {
                throw new DirectoryNotFoundException(
                    "FixWorld could not locate the RimWorld game directory.");
            }

            string toolsRoot = Path.Combine(
                this.modRoot,
                "Tools",
                "Windows-x64");
            paths = new PreloaderInstallationPaths(
                gameRoot,
                Path.Combine(
                    toolsRoot,
                    "Doorstop-4.4.0",
                    "winhttp.dll"),
                Path.Combine(toolsRoot, "FixWorld.Preloader.dll"));
        }

        internal PreloaderState GetState()
        {
            return PreloaderInstallation.GetState(paths);
        }

        internal bool EnsureActive()
        {
            PreloaderState state = GetState();
            if (state.ActiveThisLaunch)
            {
                if (!PreloaderTimelineContract.RuntimeOwnsModBoot())
                {
                    Log.Error(
                        "[FixWorld] Doorstop is active, but FixWorld.Runtime did " +
                        "not claim the mod-loading pipeline. FixWorld remains " +
                        "disabled for this launch and RimWorld continues with " +
                        "its original loader.");
                    return false;
                }

                try
                {
                    PreloaderInstallation.ConfirmStarted(paths);
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[FixWorld] The active early loader needs repair before " +
                        "the next launch: " + exception.Message);
                }

                return true;
            }

            if (state.Status == PreloaderStatus.Enabled)
            {
                Log.Error(
                    state.RestartPending
                        ? "[FixWorld] The early-loader restart did not activate " +
                          "FixWorld. It will not restart RimWorld again. " +
                          state.Message
                        : "[FixWorld] The early loader is enabled but did not " +
                          "start. FixWorld will not restart RimWorld. " +
                          state.Message);
                return false;
            }

            if (state.Status != PreloaderStatus.NotInstalled &&
                state.Status != PreloaderStatus.Disabled &&
                state.Status != PreloaderStatus.Incomplete)
            {
                Log.Error(
                    "[FixWorld] The required early loader could not be installed. " +
                    state.Message);
                return false;
            }

            try
            {
                RemoveLegacyModAssembly();
                state = PreloaderInstallation.Install(paths);
                if (state.Status != PreloaderStatus.Enabled ||
                    !state.RestartPending)
                {
                    throw new InvalidOperationException(state.Message);
                }

                Log.Message(
                    "[FixWorld] Installed the required early loader. Restarting " +
                    "RimWorld so FixWorld.Runtime can own the mod-loading pipeline.");
                GenCommandLine.Restart();
                return false;
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FixWorld] Could not install and activate the required early " +
                    "loader: " + exception);
                return false;
            }
        }

        private void RemoveLegacyModAssembly()
        {
            string assemblies = Path.Combine(modRoot, "Assemblies");
            MigrationCleanup.DeleteFiles(
                Path.Combine(assemblies, "FixWorld.dll"),
                Path.Combine(assemblies, "FixWorld.pdb"));
        }
    }
}
