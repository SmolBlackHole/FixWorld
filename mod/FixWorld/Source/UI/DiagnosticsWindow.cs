// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FixWorld.Bootstrap;
using FixWorld.Settings;
using FixWorld.Telemetry;
using FixWorld.Textures;
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
        private enum Page { Overview, Dds, Settings, Details }
        private static readonly string[] PageNames = { "Overview", "DDS cache", "Settings", "Technical details" };
        private readonly Vector2[] scroll = new Vector2[4];
        private readonly float[] heights = new float[4];
        private Page selected;
        private SettingsPanel generalSettings, ddsSettings;
        private string report = "", detailId;
        private float nextRefresh;
        private const float BodyX = 182f;

        public DiagnosticsWindow()
        {
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
            var existing = Find.WindowStack.WindowOfType<DiagnosticsWindow>();
            if (existing != null)
                Find.WindowStack.TryRemove(existing);
            else
                Find.WindowStack.Add(new DiagnosticsWindow());
        }
        public override void PostClose()
        {
            generalSettings?.Dispose();
            ddsSettings?.Dispose();
            FixWorldController.Instance.Settings.SaveChanges();
            base.PostClose();
        }
        private void EnsureSettings()
        {
            var pack = FixWorldController.OwnSettingsPack;
            if (pack == null)
                return;
            var settings = FixWorldController.Instance.Dds?.Settings;
            if (generalSettings == null)
                generalSettings = new SettingsPanel(pack, h => settings?.Owns(h) != true);
            if (ddsSettings == null && settings != null)
                ddsSettings = new SettingsPanel(pack, settings.Owns);
        }
        public override void DoWindowContents(Rect rect)
        {
            var font = Text.Font;
            var anchor = Text.Anchor;
            var color = GUI.color;
            bool enabled = GUI.enabled;
            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(0, 0, rect.width - 32, 35), "FixWorld");
                Text.Font = GameFont.Small;
                Widgets.DrawBoxSolid(new Rect(0, 42, rect.width, 2), UiTheme.Accent);
                EnsureSettings();
                for (int i = 0; i < PageNames.Length; i++)
                    Navigation((Page)i, 58 + i * 38);
                if (Widgets.ButtonText(new Rect(0, rect.height - 36, 158, 30), "Write to log"))
                {
                    using var output = new StringWriter();
                    FixWorldController.Instance.Diagnostics?.Store.WriteLog(output);
                    Log.Message("[FixWorld diagnostics]\n" + output);
                }
                float width = rect.width - BodyX;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(BodyX, 56, width, 32), PageNames[(int)selected]);
                Text.Font = GameFont.Small;
                string caption = selected == Page.Settings ? "Library settings. Changes save when you leave this page."
                    : selected == Page.Dds ? "Cached textures and background conversion."
                    : selected == Page.Details ? "Published telemetry, without display formatting."
                    : "Current runtime status. Measurements refresh twice per second.";
                Label(new Rect(BodyX, 91, width, 40), caption, UiTheme.Muted);
                var body = new Rect(BodyX, 135, width, rect.height - 145);
                if (selected == Page.Settings)
                {
                    if (generalSettings == null)
                        Widgets.Label(body, "Settings are not available yet.");
                    else
                    {
                        generalSettings.Draw(new Rect(body.x, body.y, body.width, body.height - 42), false);
                        if (Widgets.ButtonText(new Rect(body.x, body.yMax - 32, 150, 30), "Reset settings"))
                            generalSettings.ResetToDefaults();
                    }
                    return;
                }
                if (selected == Page.Dds)
                {
                    DrawMaintenance(new Rect(body.x, body.yMax - 34, body.width, 30));
                    body.height -= 46;
                }
                if (selected == Page.Details)
                    DrawDetailsSelector(ref body);
                int page = (int)selected;
                var content = new Rect(0, 0, body.width - 18, Mathf.Max(body.height, heights[page]));
                Widgets.BeginScrollView(body, ref scroll[page], content);
                try
                {
                    float y = 0;
                    if (selected == Page.Overview)
                        DrawOverview(content.width, ref y);
                    else if (selected == Page.Dds)
                        DrawDds(content.width, ref y);
                    else
                    {
                        RefreshReport();
                        y = Text.CalcHeight(report, content.width);
                        Widgets.Label(new Rect(0, 0, content.width, y), report);
                    }
                    heights[page] = y + 8;
                }
                finally { Widgets.EndScrollView(); }
            }
            finally { Text.Font = font; Text.Anchor = anchor; GUI.color = color; GUI.enabled = enabled; }
        }
        private void Navigation(Page page, float y)
        {
            var bounds = new Rect(0, y, 158, 32);
            if (page == selected)
                Widgets.DrawBoxSolid(bounds, UiTheme.Completed);
            else if (Mouse.IsOver(bounds))
                Widgets.DrawHighlight(bounds);
            Widgets.Label(bounds.ContractedBy(7, 4), PageNames[(int)page]);
            if (Widgets.ButtonInvisible(bounds) && selected != page)
            {
                generalSettings?.CommitPending();
                ddsSettings?.CommitPending();
                GUI.FocusControl(null);
                FixWorldController.Instance.Settings.SaveChanges();
                selected = page;
                nextRefresh = 0;
                // Each page keeps its scroll and input objects across snapshot refreshes.
            }
        }
        private void DrawOverview(float width, ref float y)
        {
            var controller = FixWorldController.Instance;
            Section(width, ref y, "Runtime");
            Row(width, ref y, "Bootstrap", BootSession.Current.Phase.ToString());
            var library = controller.Diagnostics?.Snapshot;
            if (library != null)
            {
                Row(width, ref y, "Callback errors", Count(library.Errors));
                Row(width, ref y, "Game tick notifications", Count(library.Ticks));
                Row(width, ref y, "Delayed callbacks", Count(library.State.DelayedCallbacks));
            }
            var loading = controller.Loading?.Current;
            Section(width, ref y, "Loading");
            if (loading == null)
                Note(width, ref y, "No loading snapshot has been published yet.");
            else
            {
                Row(width, ref y, "Stage", LoadingProgress.Names[(int)loading.Stage]);
                Row(width, ref y, "Elapsed", (controller.Loading.Elapsed(loading) / 1000).ToString("N1") + " s");
                if (!string.IsNullOrEmpty(loading.Failure))
                    Note(width, ref y, loading.Failure, true);
            }
            Section(width, ref y, "DDS cache");
            var dds = controller.Dds?.PublishedSnapshot;
            if (dds == null)
                Note(width, ref y, "DDS has not published a snapshot yet.");
            else
            {
                Row(width, ref y, "Worker", DdsStatus(dds));
                Row(width, ref y, "Loaded from cache", Count(dds.Hits));
                Row(width, ref y, "Cache size", Bytes(dds.CacheBytes));
                if (dds.Failed > 0 || !string.IsNullOrEmpty(dds.Error))
                    Note(width, ref y, "DDS needs attention. Open DDS cache for errors and retry.", true);
                if (!string.IsNullOrEmpty(dds.ReserveWarning))
                    Note(width, ref y, dds.ReserveWarning, true);
            }
            Note(width, ref y, "Detailed counters and profiling measurements remain available under Technical details.");
        }
        private void DrawDds(float width, ref float y)
        {
            var data = FixWorldController.Instance.Dds?.PublishedSnapshot;
            if (data == null)
            { Note(width, ref y, "Waiting for the first DDS snapshot."); return; }
            Row(width, ref y, "Worker", DdsStatus(data));
            if (!string.IsNullOrEmpty(data.Error))
                Note(width, ref y, data.Error, true);
            Section(width, ref y, "Storage");
            Row(width, ref y, "Cache used / limit", Bytes(data.CacheBytes) + " / " + Bytes(data.MaxCacheBytes));
            var bar = new Rect(8, y, width - 16, 5);
            Widgets.DrawBoxSolid(bar, UiTheme.Pending);
            float ratio = data.MaxCacheBytes > 0 ? Mathf.Clamp01((float)((double)data.CacheBytes / data.MaxCacheBytes)) : 0;
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), UiTheme.Accent);
            y += 16;
            Row(width, ref y, "Disk free / reserve", Bytes(data.AvailableFreeBytes) + " / " + Bytes(data.MinimumFreeBytes));
            Row(width, ref y, "Effective cache budget", Bytes(data.EffectiveBudgetBytes));
            if (!string.IsNullOrEmpty(data.ReserveWarning))
                Note(width, ref y, data.ReserveWarning, true);
            Note(width, ref y, "The free-space reserve takes priority over the cache limit. Maintenance runs in the background.");
            Section(width, ref y, "Cache settings");
            if (data.MaximumOverridden || data.MinimumFreeOverridden)
                Note(width, ref y, "Environment overrides are active for " +
                    (data.MaximumOverridden ? "cache limit" : "") +
                    (data.MaximumOverridden && data.MinimumFreeOverridden ? " and " : "") +
                    (data.MinimumFreeOverridden ? "free-space reserve" : "") +
                    ". The effective values above override saved settings.", true);
            if (ddsSettings == null)
                Note(width, ref y, "Settings are not available yet.");
            else
            {
                y += ddsSettings.DrawContents(new Rect(0, y, width, 250));
                if (Widgets.ButtonText(new Rect(8, y + 5, 160, 28), "Reset cache settings"))
                    ddsSettings.ResetToDefaults();
                y += 45;
            }
            Section(width, ref y, "This session");
            Row(width, ref y, "Loaded / missing", Count(data.Hits) + " / " + Count(data.Misses));
            Row(width, ref y, "Created / failed", Count(data.Created) + " / " + Count(data.Failed));
            Row(width, ref y, "Excluded / unsupported", Count(data.Excluded) + " / " + Count(data.Unsupported));
            Row(width, ref y, "Skipped for budget", Count(data.BudgetSkipped));
            Row(width, ref y, "Background build time", (data.BuildMilliseconds / 1000d).ToString("N1") + " s");
            Section(width, ref y, "Location");
            Note(width, ref y, data.Root ?? "Cache location is not available yet.");
        }
        private void DrawMaintenance(Rect rect)
        {
            var dds = FixWorldController.Instance.Dds;
            bool wasEnabled = GUI.enabled;
            try
            {
                GUI.enabled = wasEnabled && dds?.CanMaintain == true;
                float half = (rect.width - 12) / 2;
                if (Widgets.ButtonText(new Rect(rect.x, rect.y, half, rect.height), "Clear DDS cache"))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Delete FixWorld's generated DDS packs? Original textures stay untouched. Restart to rebuild.",
                        () => Find.WindowStack.Add(new Dialog_MessageBox(dds.ClearCache()))));
                if (Widgets.ButtonText(new Rect(rect.x + half + 12, rect.y, half, rect.height), "Retry DDS builds"))
                    Find.WindowStack.Add(new Dialog_MessageBox(dds.RetryFailedBuilds()));
            }
            finally { GUI.enabled = wasEnabled; }
        }
        private void DrawDetailsSelector(ref Rect body)
        {
            if (Widgets.ButtonText(new Rect(body.x, body.y, body.width, 30), detailId ?? "All telemetry"))
            {
                var options = new List<FloatMenuOption> { new("All telemetry", () => SelectDetails(null)) };
                var registrations = FixWorldController.Instance.Diagnostics?.Store.Registrations;
                if (registrations != null)
                    foreach (var registration in registrations)
                    {
                        string id = registration.Id;
                        options.Add(new FloatMenuOption(id, () => SelectDetails(id)));
                    }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            body.y += 42;
            body.height -= 42;
        }
        private void SelectDetails(string id) { detailId = id; nextRefresh = 0; scroll[(int)Page.Details] = Vector2.zero; }
        private void RefreshReport()
        {
            if (Time.realtimeSinceStartup < nextRefresh)
                return;
            nextRefresh = Time.realtimeSinceStartup + .5f;
            using var output = new StringWriter();
            var store = FixWorldController.Instance.Diagnostics?.Store;
            if (detailId == null)
                store?.WriteLog(output);
            else if (store != null)
                foreach (var registration in store.Registrations)
                    if (registration.Id == detailId)
                        registration.Write(new TelemetryWriter(output, false));
            report = output.ToString();
            if (report.Length == 0)
                report = "No published measurements yet.";
        }
        private static string DdsStatus(TextureDdsCacheSnapshot data) => !data.Enabled ? "Disabled"
            : data.Busy ? "Working" : data.MaintenancePending ? "Maintenance queued" : "Idle";
        private static string Count(long value) => value.ToString("N0");
        private static string Bytes(long value) => value < 0 ? "Not measured" : (value / 1073741824d).ToString("N2", CultureInfo.CurrentCulture) + " GiB";
        private static void Label(Rect rect, string text, Color color)
        {
            var before = GUI.color;
            GUI.color = color;
            try
            { Widgets.Label(rect, text); }
            finally { GUI.color = before; }
        }
        private static void Section(float width, ref float y, string title)
        {
            y += 12;
            Label(new Rect(8, y, width - 16, 26), title, UiTheme.Accent);
            y += 29;
            Widgets.DrawBoxSolid(new Rect(8, y, width - 16, 1), UiTheme.Pending);
            y += 9;
        }
        private static void Row(float width, ref float y, string name, string value)
        {
            float labelWidth = width * .46f;
            float height = Mathf.Max(30, Mathf.Max(Text.CalcHeight(name, labelWidth - 16),
                Text.CalcHeight(value, width - labelWidth - 16)) + 8);
            Widgets.DrawBoxSolid(new Rect(0, y, width, height - 2), UiTheme.Row);
            Label(new Rect(8, y + 4, labelWidth - 16, height - 8), name, UiTheme.Muted);
            Widgets.Label(new Rect(labelWidth, y + 4, width - labelWidth - 8, height - 8), value);
            y += height;
        }
        private static void Note(float width, ref float y, string text, bool warning = false)
        {
            float height = Text.CalcHeight(text, width - 16);
            Label(new Rect(8, y + 5, width - 16, height), text, warning ? UiTheme.Warning : UiTheme.Muted);
            y += height + 16;
        }
    }
}
