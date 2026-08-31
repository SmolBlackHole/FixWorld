using System;
using FixWorld.Preloader;
using UnityEngine;
using Verse;

namespace FixWorld
{
    public sealed class FixWorldMod : Mod
    {
        private readonly FixWorldSettings settings;

        public FixWorldMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<FixWorldSettings>();
            FixWorldBootstrap.Initialize(this, content, settings);
        }

        public override string SettingsCategory()
        {
            return "FixWorld";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("Optional Windows early loader");
            PreloaderState state = PreloaderManager.GetState();
            listing.Label(state.Message);
            listing.Gap();

            try
            {
                if ((state.Status == PreloaderStatus.NotInstalled ||
                     state.Status == PreloaderStatus.Disabled) &&
                    listing.ButtonText("Enable for next launch"))
                {
                    PreloaderManager.InstallOrEnable();
                    settings.PreloaderPromptDismissed = true;
                    WriteSettings();
                }
                else if (state.Status == PreloaderStatus.Enabled &&
                         listing.ButtonText("Disable for next launch"))
                {
                    PreloaderManager.Disable();
                }
            }
            catch (Exception exception)
            {
                Log.Error("[FixWorld] Could not change early-loader state: " + exception);
            }

            listing.Gap();
            listing.Label(
                "Physical removal: close RimWorld, then run " +
                "Tools/Windows-x64/FixWorld.Preloader.Tool.exe uninstall.");
            listing.GapLine();
            listing.Label("DDS texture cache");
            listing.Label(
                "Maximum size: " + settings.DdsCacheMaxGiB.ToString("0") +
                " GiB (applies on the next launch)");
            float cacheLimit = Mathf.Round(listing.Slider(
                settings.DdsCacheMaxGiB,
                1.0f,
                64.0f));
            if (Math.Abs(cacheLimit - settings.DdsCacheMaxGiB) >= 0.5f)
            {
                settings.DdsCacheMaxGiB = cacheLimit;
                WriteSettings();
            }
            listing.End();
        }
    }
}
