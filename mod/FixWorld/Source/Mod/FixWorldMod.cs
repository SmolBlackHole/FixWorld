using System;
using FixWorld.Preloader;
using FixWorld.RuntimeBridge;
using FixWorld.UI;
using UnityEngine;
using Verse;

namespace FixWorld
{
    public sealed class FixWorldMod : Mod
    {
        private readonly PreloaderService preloader;
        private readonly RuntimeContract runtime;
        private readonly FixWorldSettings settings;

        internal static FixWorldMod Instance { get; private set; }

        public FixWorldMod(ModContentPack content) : base(content)
        {
            Instance = this;
            settings = GetSettings<FixWorldSettings>();
            preloader = new PreloaderService(content.RootDir);
            if (!preloader.EnsureActive())
            {
                return;
            }

            runtime = RuntimeContract.BindLoaded();
            runtime.AttachMod(
                this,
                content,
                settings.DdsCacheMaxGiB);
        }

        public override string SettingsCategory()
        {
            return "FixWorld";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("Required Windows early loader");
            PreloaderState state = preloader.GetState();
            listing.Label(state.Message);
            listing.Gap();
            listing.Label(
                "Physical removal: close RimWorld, then run " +
                "Tools/Windows-x64/FixWorld.Tool.exe preloader uninstall.");
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
            listing.GapLine();
            if (listing.ButtonText("Open FixWorld diagnostics"))
            {
                DiagnosticsWindow.Toggle();
            }
            listing.End();
        }

        internal string GetDiagnosticsText()
        {
            return runtime == null
                ? "FixWorld.Runtime is not active for this launch."
                : runtime.GetDiagnosticsText();
        }

        internal string ClearDdsCache()
        {
            return runtime == null
                ? "FixWorld.Runtime is not active for this launch."
                : runtime.ClearDdsCache();
        }

        internal string RetryFailedDdsBuilds()
        {
            return runtime == null
                ? "FixWorld.Runtime is not active for this launch."
                : runtime.RetryFailedDdsBuilds();
        }
    }
}
