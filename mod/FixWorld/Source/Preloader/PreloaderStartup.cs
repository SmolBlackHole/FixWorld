using System;
using Verse;

namespace FixWorld.Preloader
{
    internal static class PreloaderStartup
    {
        private const string RestartAttemptVariable =
            "FIXWORLD_PRELOADER_RESTART_ATTEMPTED";

        internal static bool EnsureInstalled(string modRoot)
        {
            PreloaderManager.Configure(modRoot);
            PreloaderState state = PreloaderManager.GetState();
            if (state.Status == PreloaderStatus.Enabled &&
                state.ActiveThisLaunch)
            {
                return true;
            }

            if (state.ActiveThisLaunch)
            {
                Log.Error(
                    "[FixWorld] The active Doorstop installation is invalid. " +
                    state.Message);
                return false;
            }

            if (RestartAttempted())
            {
                Log.Error(
                    "[FixWorld] Doorstop was still inactive after the automatic " +
                    "restart. FixWorld stopped to prevent a restart loop. " +
                    state.Message);
                return false;
            }

            if (state.Status != PreloaderStatus.NotInstalled &&
                state.Status != PreloaderStatus.Disabled &&
                state.Status != PreloaderStatus.Incomplete &&
                state.Status != PreloaderStatus.Enabled)
            {
                Log.Error(
                    "[FixWorld] The required early loader could not be installed. " +
                    state.Message);
                return false;
            }

            try
            {
                if (state.Status != PreloaderStatus.Enabled)
                {
                    state = PreloaderManager.Install();
                }

                if (state.Status != PreloaderStatus.Enabled)
                {
                    throw new InvalidOperationException(state.Message);
                }

                MarkRestartAttempted();
                Log.Message(
                    "[FixWorld] Installed the required early loader. Restarting " +
                    "RimWorld so FixWorld.Loader can own the mod-loading pipeline.");
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

        private static bool RestartAttempted()
        {
            return string.Equals(
                Environment.GetEnvironmentVariable(RestartAttemptVariable),
                "1",
                StringComparison.Ordinal);
        }

        private static void MarkRestartAttempted()
        {
            Environment.SetEnvironmentVariable(
                RestartAttemptVariable,
                "1",
                EnvironmentVariableTarget.Process);
        }

    }
}
