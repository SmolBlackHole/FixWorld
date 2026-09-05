// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using System.IO;
using FixWorld.Telemetry;
using RimWorld;
using UnityEngine;
using Verse;

namespace FixWorld.UI
{
    public sealed class MainButtonWorker_Diagnostics : MainButtonWorker
    {
        public override void Activate() => DiagnosticsWindow.Toggle();
    }

    public sealed class DiagnosticsWindow : Window
    {
        private Vector2 scroll;
        private Vector2 navigationScroll;
        private string selected;
        private string report = "No published measurements yet.";
        private float nextRefresh;
        public DiagnosticsWindow()
        {
            forcePause = false;
            closeOnCancel = true;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            onlyOneOfTypeAllowed = true;
        }
        public override Vector2 InitialSize => new(980f, 680f);
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            windowRect.width = Mathf.Min(Verse.UI.screenWidth, Mathf.Max(640f, windowRect.width));
            windowRect.height = Mathf.Min(Verse.UI.screenHeight, Mathf.Max(440f, windowRect.height));
            windowRect.x = Mathf.Clamp(windowRect.x, 0, Verse.UI.screenWidth - windowRect.width);
            windowRect.y = Mathf.Clamp(windowRect.y, 0, Verse.UI.screenHeight - windowRect.height);
        }
        internal static void Toggle()
        {
            DiagnosticsWindow existing = Find.WindowStack.WindowOfType<DiagnosticsWindow>();
            if (existing != null)
            {
                Find.WindowStack.TryRemove(existing);
            }
            else
            {
                Find.WindowStack.Add(new DiagnosticsWindow());
            }
        }
        public override void DoWindowContents(Rect rect)
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(0, 0, rect.width - 170f, 35f), "FixWorld diagnostics");
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(new Rect(rect.width - 165f, 0, 130f, 30f), "Settings"))
                {
                    Find.WindowStack.Add(new Settings.Dialog_ModSettings(FixWorldController.OwnSettingsPack));
                }

                Widgets.DrawBoxSolid(new Rect(0, 42f, rect.width, 2f), LoadingProgressUi.Accent);
                TelemetryStore store = FixWorldController.Instance.Diagnostics?.Store;
                if (store == null)
                { Widgets.Label(new Rect(0, 60f, rect.width, 40f), "FixWorld services are not available."); return; }
                IReadOnlyList<TelemetryRegistration> registrations = store.Registrations;
                var nav = new Rect(0, 58f, 184f, rect.height - 110f);
                Widgets.BeginScrollView(nav, ref navigationScroll, new Rect(0, 0, 166f, Mathf.Max(nav.height, (registrations.Count + 1) * 34f)));
                try
                {
                    Navigation("Overview", null, 0);
                    for (int i = 0; i < registrations.Count; i++)
                    {
                        Navigation(registrations[i].Id.Replace("fixworld.", ""), registrations[i].Id, (i + 1) * 34f);
                    }
                }
                finally { Widgets.EndScrollView(); }
                if (Time.realtimeSinceStartup >= nextRefresh)
                {
                    nextRefresh = Time.realtimeSinceStartup + .5f;
                    using var output = new StringWriter();
                    if (selected == null)
                    {
                        store.WriteLog(output);
                    }
                    else
                    {
                        var writer = new TelemetryWriter(output, false);
                        foreach (TelemetryRegistration registration in registrations)
                        {
                            if (registration.Id == selected)
                            {
                                registration.Write(writer);
                            }
                        }
                    }
                    report = output.ToString();
                    if (report.Length == 0)
                    {
                        report = "No published measurements yet.";
                    }
                    // Refresh data without replacing the window or its scroll position.
                }
                var viewport = new Rect(202f, 58f, Mathf.Max(100, rect.width - 202f), rect.height - 110f);
                var content = new Rect(0, 0, viewport.width - 18f, Mathf.Max(viewport.height, Text.CalcHeight(report, viewport.width - 18f)));
                Widgets.BeginScrollView(viewport, ref scroll, content);
                try
                { Widgets.Label(content, report); }
                finally { Widgets.EndScrollView(); }
                if (Widgets.ButtonText(new Rect(202f, rect.height - 36f, 160f, 30f), "Write to log"))
                {
                    Log.Message("[FixWorld diagnostics]\n" + report);
                }

                if (selected == "fixworld.dds" && FixWorldController.Instance.Dds != null)
                {
                    var dds = FixWorldController.Instance.Dds;
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && dds.CanMaintain;
                    if (Widgets.ButtonText(new Rect(378f, rect.height - 36f, 155f, 30f), "Clear DDS cache"))
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "Delete FixWorld's generated DDS packs? Source textures are not changed. Restart to rebuild.",
                            () => Find.WindowStack.Add(new Dialog_MessageBox(dds.ClearCache()))));
                    if (Widgets.ButtonText(new Rect(545f, rect.height - 36f, 155f, 30f), "Retry DDS builds"))
                        Find.WindowStack.Add(new Dialog_MessageBox(dds.RetryFailedBuilds()));
                    GUI.enabled = enabled;
                }
                else
                {
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(378f, rect.height - 30f, rect.width - 378f, 24f), "Published snapshots / refresh 0.5 s");
                }
            }
            finally { Text.Font = font; Text.Anchor = anchor; GUI.color = color; }
        }
        private void Navigation(string label, string id, float y)
        {
            var bounds = new Rect(0, y, 162f, 30f);
            if (id == selected)
            {
                Widgets.DrawBoxSolid(bounds, LoadingProgressUi.Completed);
            }

            Widgets.Label(bounds.ContractedBy(7f, 2f), label);
            if (Widgets.ButtonInvisible(bounds))
            { selected = id; scroll = Vector2.zero; nextRefresh = 0; }
        }
    }
}
