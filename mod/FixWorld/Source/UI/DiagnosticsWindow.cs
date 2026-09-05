// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FixWorld.Profiling;
using FixWorld.Bootstrap;
using FixWorld.Core;
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
        private enum Page { Overview, Dds, Loading, Mods, Profiling, Doorstop, Settings, Details }
        private static readonly string[] PageNames = { "Overview", "DDS cache", "Loading stages", "Mod loading", "Profiling", "Doorstop", "Settings", "Technical details" };
        private readonly Vector2[] scroll = new Vector2[PageNames.Length];
        private readonly float[] heights = new float[PageNames.Length];
        private string modFilter = "", expandedMod;
        private ProfileSnapshot<ProfileKey> shownProfile;
        private ProfileMeasurement<ProfileKey>[] profileRows = Array.Empty<ProfileMeasurement<ProfileKey>>();
        private Page selected;
        private SettingsPanel generalSettings, ddsSettings;
        private string report = "", detailId;
        private float nextRefresh;
        private string installationReadError;
        private bool maintenanceRequested;
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
                    : selected == Page.Loading ? "Observed stage durations. Missing observations are not zero-cost work."
                    : selected == Page.Mods ? "Measured loading sections, not complete per-mod startup cost."
                    : selected == Page.Profiling ? "Inclusive timings, sorted by total time. Nested scopes may overlap."
                    : selected == Page.Doorstop ? "Early startup installation and removal."
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
                if (selected == Page.Mods)
                {
                    Widgets.Label(new Rect(body.x, body.y, 85, 30), "Filter mods");
                    modFilter = Widgets.TextField(new Rect(body.x + 90, body.y, body.width - 90, 30), modFilter);
                    body.y += 42;
                    body.height -= 42;
                }
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
                    else if (selected == Page.Loading)
                        DrawLoading(content.width, ref y);
                    else if (selected == Page.Mods)
                        DrawMods(content.width, ref y);
                    else if (selected == Page.Profiling)
                        DrawProfiling(content.width, ref y);
                    else if (selected == Page.Doorstop)
                        DrawDoorstop(content.width, ref y);
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
                if (page == Page.Doorstop)
                    RefreshInstallation();
                nextRefresh = 0;
                // Each page keeps its scroll and input objects across snapshot refreshes.
            }
        }
        private void DrawOverview(float width, ref float y)
        {
            var controller = FixWorldController.Instance;
            Section(width, ref y, "Runtime");
            Row(width, ref y, "Bootstrap", BootSession.Current.Phase.ToString());
            Row(width, ref y, "Startup", "Early via Doorstop");
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
            if (data.PlannedMods > 0)
            {
                Row(width, ref y, "Mods processed / planned", Count(data.ProcessedMods) + " / " + Count(data.PlannedMods));
                Row(width, ref y, "Mods remaining", Count(data.RemainingMods));
                if (!string.IsNullOrEmpty(data.CurrentMod))
                    Row(width, ref y, "Working on", data.CurrentMod);
                Note(width, ref y, "Processed includes failed and skipped builds. Mod sizes differ; this is not a time estimate.");
            }
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

        private void RefreshInstallation()
        {
            try
            { BootstrapIntegration.RefreshInstallation(); installationReadError = null; }
            catch (Exception error) { installationReadError = error.Message; }
        }

        private void DrawDoorstop(float width, ref float y)
        {
            Row(width, ref y, "This launch", "Early via Doorstop");
            if (Widgets.ButtonText(new Rect(8, y + 4, 160, 30), "Refresh status"))
                RefreshInstallation();
            y += 42;
            if (installationReadError != null)
            { Note(width, ref y, "Cannot inspect installation: " + installationReadError, true); return; }
            var state = BootstrapIntegration.LastInstallationState;
            if (!state.HasValue)
            { Note(width, ref y, "Refresh to inspect the installation."); return; }
            Row(width, ref y, "Installation", state.Value.Status.ToString());
            Note(width, ref y, state.Value.Message, state.Value.Status == InstallationStatus.Conflict);
            if (state.Value.RestartPending)
                Note(width, ref y, "An earlier installation or removal is awaiting completion. Explicit maintenance can repair owned files.", true);
            if (BootstrapIntegration.MaintenanceError != null)
                Note(width, ref y, BootstrapIntegration.MaintenanceError, true);
            if (maintenanceRequested)
                Note(width, ref y, "Shutdown requested. Waiting for RimWorld to exit before changing files.");

            Section(width, ref y, "Maintenance");
            Note(width, ref y, "Uninstall closes RimWorld and removes FixWorld's owned Doorstop files. Remove or disable FixWorld before launching again, otherwise its normal installer will install Doorstop and restart the game. Settings, saves and DDS packs stay on disk.");
            Note(width, ref y, "Save your game first. Install and reinstall restart RimWorld; uninstall leaves it closed. None of these actions saves your colony.", true);
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && !maintenanceRequested && state.Value.Status != InstallationStatus.Conflict;
                var action = state.Value.Status == InstallationStatus.Missing ? InstallationAction.Install : InstallationAction.Reinstall;
                if (Widgets.ButtonText(new Rect(8, y, width - 16, 30), action == InstallationAction.Install ? "Install Doorstop..." : "Reinstall Doorstop..."))
                    ConfirmMaintenance(action);
                y += 40;
                GUI.enabled = GUI.enabled && state.Value.Status != InstallationStatus.Missing;
                if (Widgets.ButtonText(new Rect(8, y, width - 16, 30), "Uninstall Doorstop..."))
                    ConfirmMaintenance(InstallationAction.Uninstall);
                y += 40;
            }
            finally { GUI.enabled = enabled; }
        }

        private void ConfirmMaintenance(InstallationAction action)
        {
            bool uninstall = action == InstallationAction.Uninstall;
            string effect = uninstall
                ? "The game will stay closed so you can remove FixWorld. If FixWorld remains enabled, the next launch installs Doorstop again and restarts."
                : "FixWorld will start early through Doorstop on the next launch.";
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                action + " Doorstop and " + (uninstall ? "close" : "restart") + " RimWorld? Unsaved colony progress will be lost. " + effect,
                () =>
                {
                    generalSettings?.CommitPending();
                    ddsSettings?.CommitPending();
                    FixWorldController.Instance.Settings.SaveChanges();
                    maintenanceRequested = BootstrapIntegration.RequestMaintenance(action);
                }));
        }

        private void DrawLoading(float width, ref float y)
        {
            var loading = FixWorldController.Instance.Loading;
            var data = loading?.Current;
            if (data == null)
            { Note(width, ref y, "No loading snapshot yet."); return; }
            Row(width, ref y, "Elapsed", (loading.Elapsed(data) / 1000).ToString("N1") + " s");
            if (!string.IsNullOrEmpty(data.Failure))
                Note(width, ref y, data.Failure, true);
            for (int i = 0; i < LoadingProgress.Names.Length; i++)
            {
                if (i == (int)LoadingStage.Complete)
                    continue;
                double duration = data.Duration(i);
                string value = duration > 0 ? duration.ToString("N1") + " ms" : "Not observed";
                if (data.Active && i == (int)data.Stage)
                    value = (Math.Max(0, loading.Elapsed(data) - data.ElapsedMilliseconds)).ToString("N1") + " ms (running)";
                else if (i > (int)data.Stage)
                    value = data.Active ? "Pending" : "Not reached";
                Row(width, ref y, LoadingProgress.Names[i], value);
            }
        }
        private void DrawProfiling(float width, ref float y)
        {
            var data = FixWorldController.Instance.Diagnostics?.Snapshot?.Profile;
            if (data == null)
            { Note(width, ref y, "No profiling snapshot yet."); return; }
            if (!ReferenceEquals(data, shownProfile))
            { shownProfile = data; profileRows = data.Where(r => r.Calls > 0).OrderByDescending(r => r.TotalStopwatchTicks).ToArray(); }
            if (profileRows.Length == 0)
            { Note(width, ref y, "No measured calls yet."); return; }
            if (width >= 580)
                TableRow(width, ref y, new[] { "Operation / owner", "Calls", "Total ms", "Avg ms", "Max ms" }, true);
            foreach (var row in profileRows)
            {
                string title = row.Key.Operation + "\n" + row.Key.Owner + " / " + row.Key.Source;
                if (row.Failures > 0)
                    title += " (" + Count(row.Failures) + " failed)";
                if (width >= 580)
                    TableRow(width, ref y, new[]{title,Count(row.Calls),row.TotalTime.TotalMilliseconds.ToString("N2"),
                        row.AverageTime.TotalMilliseconds.ToString("N3"),row.MaximumTime.TotalMilliseconds.ToString("N2")});
                else
                {
                    Section(width, ref y, title);
                    Row(width, ref y, "Calls / failures", Count(row.Calls) + " / " + Count(row.Failures));
                    Row(width, ref y, "Total / average / max (ms)", row.TotalTime.TotalMilliseconds.ToString("N2") + " / " +
                        row.AverageTime.TotalMilliseconds.ToString("N3") + " / " + row.MaximumTime.TotalMilliseconds.ToString("N2"));
                }
            }
        }
        private void DrawMods(float width, ref float y)
        {
            var data = FixWorldController.Instance.ModLoading?.Snapshot;
            if (data == null)
            { Note(width, ref y, "No mod loading measurements yet. New hooks take effect after restarting the game."); return; }
            if (!string.IsNullOrEmpty(data.Failure))
                Note(width, ref y, "Mod loading observations unavailable: " + data.Failure, true);
            Note(width, ref y, "Assemblies/setup, constructors, XML file loading and deferred content are measured. Global Def processing and arbitrary deferred callbacks are not attributed.");
            Note(width, ref y, "Click a mod name to show phase timings and captured messages.");
            int shown = 0;
            foreach (var mod in data.Mods)
            {
                if (mod.Name.IndexOf(modFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    mod.Id.IndexOf(modFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                shown++;
                float top = y;
                Row(width, ref y, mod.Name, mod.TotalMilliseconds.ToString("N1") + " ms observed");
                if (Widgets.ButtonInvisible(new Rect(0, top, width, y - top)))
                    expandedMod = expandedMod == mod.Id ? null : mod.Id;
                Row(width, ref y, "Errors / warnings", Count(mod.Errors) + " / " + Count(mod.Warnings));
                if (expandedMod != mod.Id)
                    continue;
                Note(width, ref y, mod.Id);
                for (int i = 0; i < 4; i++)
                    Row(width, ref y, ((ModLoadPart)i).ToString(), mod.Times[i].ToString("N1") + " ms");
                Note(width, ref y, "Messages occurred in this loading context; this does not prove fault. Up to five distinct samples are kept.");
                foreach (string message in mod.Messages)
                    Note(width, ref y, message, true);
                if (mod.Messages.Count == 0)
                    Note(width, ref y, "No captured messages. This is not a complete mod error audit.");
            }
            if (shown == 0)
                Note(width, ref y, "No mods match this filter.");
        }
        private static void TableRow(float width, ref float y, string[] values, bool heading = false)
        {
            float[] fractions = { .42f, .12f, .16f, .14f, .16f };
            float height = 30;
            for (int i = 0; i < values.Length; i++)
                height = Mathf.Max(height, Text.CalcHeight(values[i], width * fractions[i] - 12) + 8);
            if (!heading)
                Widgets.DrawBoxSolid(new Rect(0, y, width, height - 2), UiTheme.Row);
            float x = 0;
            for (int i = 0; i < values.Length; i++)
            { Label(new Rect(x + 6, y + 4, width * fractions[i] - 12, height - 8), values[i], heading ? UiTheme.Accent : GUI.color); x += width * fractions[i]; }
            y += height;
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
            float height = Mathf.Max(26, Text.CalcHeight(title, width - 16));
            Label(new Rect(8, y, width - 16, height), title, UiTheme.Accent);
            y += height + 3;
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
