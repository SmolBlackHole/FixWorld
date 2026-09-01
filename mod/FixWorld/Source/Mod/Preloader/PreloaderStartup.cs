using System;
using Verse;

namespace FixWorld.Preloader
{
    internal static class PreloaderStartup
    {
        internal static bool EnsureInstalled(string modRoot)
        {
            PreloaderManager.Configure(modRoot);
            PreloaderState state = PreloaderManager.GetState();
            if (state.Status == PreloaderStatus.Enabled &&
                state.ActiveThisLaunch)
            {
                if (PreloaderTimelineContract.RuntimeOwnsModBoot())
                {
                    return true;
                }

                Log.Error(
                    "[FixWorld] Doorstop is active, but FixWorld.Runtime did not " +
                    "claim the mod-loading pipeline. FixWorld remains disabled " +
                    "for this launch and RimWorld continues with its original loader.");
                return false;
            }

            if (state.ActiveThisLaunch)
            {
                Log.Error(
                    "[FixWorld] The active Doorstop installation is invalid. " +
                    state.Message);
                return false;
            }

            if (state.Status == PreloaderStatus.Enabled)
            {
                Log.Error(
                    "[FixWorld] The early loader is enabled but did not start. " +
                    "FixWorld will not restart RimWorld again. " +
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
    }
}
