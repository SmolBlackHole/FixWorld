using System;
using RimWorld;
using Verse;

namespace FixWorld.Preloader
{
    internal static class PreloaderPrompt
    {
        private static FixWorldMod mod;
        private static FixWorldSettings settings;
        private static bool shown;

        internal static void Configure(FixWorldMod owner, FixWorldSettings modSettings)
        {
            mod = owner;
            settings = modSettings;
        }

        internal static void TryShow()
        {
            if (shown || mod == null || settings == null || settings.PreloaderPromptDismissed)
            {
                return;
            }

            PreloaderState state = PreloaderManager.GetState();
            if (state.Status != PreloaderStatus.NotInstalled)
            {
                return;
            }

            shown = true;
            Find.WindowStack.Add(new Dialog_MessageBox(
                "FixWorld can optionally install the official UnityDoorstop 4.4.0 " +
                "beside RimWorldWin64.exe. This lets FixWorld measure and optimize the " +
                "startup work that happens before normal mods are created.\n\n" +
                "It becomes active on the next launch. FixWorld checks the native DLL " +
                "by SHA-256 and never overwrites an unknown winhttp.dll or Doorstop config. " +
                "The normal mod continues to work without it.",
                "Enable next launch",
                Enable,
                "Not now",
                Dismiss,
                "Optional FixWorld early loader"));
        }

        private static void Enable()
        {
            settings.PreloaderPromptDismissed = true;
            mod.WriteSettings();
            try
            {
                PreloaderManager.InstallOrEnable();
                Messages.Message(
                    "FixWorld early loader will be active on the next launch.",
                    MessageTypeDefOf.PositiveEvent,
                    false);
            }
            catch (Exception exception)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "FixWorld did not change the RimWorld directory.\n\n" + exception.Message,
                    title: "Early loader was not installed"));
            }
        }

        private static void Dismiss()
        {
            settings.PreloaderPromptDismissed = true;
            mod.WriteSettings();
        }
    }
}
