// SPDX-License-Identifier: MPL-2.0
using UnityEngine;
using Verse;

namespace FixWorld.Settings
{
    /// <summary>The library's standalone host for the reusable settings panel.</summary>
    public class Dialog_ModSettings : Window
    {
        private readonly ModSettingsPack pack;
        private SettingsPanel panel;
        public override Vector2 InitialSize => new Vector2(650f, 700f);
        public Dialog_ModSettings(ModSettingsPack pack)
        {
            this.pack = pack ?? throw new System.ArgumentNullException(nameof(pack));
            closeOnCancel = true;
            closeOnAccept = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }
        public override void PreOpen()
        {
            base.PreOpen();
            panel = new SettingsPanel(pack);
            var state = ModSettingsWindowState.Instance;
            if (state?.LastSettingsPackId == pack.ModId)
                panel.ScrollY = state.VerticalScrollPosition;
        }
        public override void PostClose()
        {
            var state = ModSettingsWindowState.Instance;
            if (state != null && panel != null)
            { state.LastSettingsPackId = pack.ModId; state.VerticalScrollPosition = panel.ScrollY; }
            panel?.Dispose();
            FixWorldController.Instance.Settings.SaveChanges();
            base.PostClose();
        }
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(10, 0, inRect.width - 30, 40), "FixWorld_settings_windowTitle".Translate());
            Text.Font = GameFont.Small;
            panel.Draw(new Rect(10, 40, inRect.width - 20, inRect.height - 86));
            if (Widgets.ButtonText(new Rect(0, inRect.height - 36, 150, 36), "FixWorld_settings_resetAll".Translate()))
                panel.ResetToDefaults();
            if (Widgets.ButtonText(new Rect(inRect.width - 150, inRect.height - 36, 150, 36), "CloseButton".Translate()))
            { panel.CommitPending(); Close(); }
        }
    }
}
