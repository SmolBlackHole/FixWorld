using System;
using FixWorld.Presentation;
using RimWorld;
using UnityEngine;
using Verse;

namespace FixWorld.UI
{
    public sealed class DiagnosticsWindow : Window
    {
        private const float FooterHeight = 24f;
        private const float HeaderHeight = 58f;
        private const float NavigationGap = 14f;
        private const float NavigationWidth = 178f;
        private const float DetailedRowHeight = 47f;
        private const float DdsActionsHeight = 66f;
        private const float RefreshIntervalSeconds = 0.5f;
        private const float RowHeight = 29f;
        private const float MinimumHeight = 440f;
        private const float MinimumWidth = 640f;

        private static readonly Vector2 DefaultSize = new Vector2(980f, 680f);
        private static readonly Color Accent = ToColor(FixWorldUiTheme.Accent);
        private static readonly Color Completed = ToColor(FixWorldUiTheme.Completed);
        private static readonly Color MutedText = ToColor(FixWorldUiTheme.MutedText);
        private static readonly Color Pending = ToColor(FixWorldUiTheme.Pending);
        private static readonly Color Row = ToColor(FixWorldUiTheme.Row);
        private static readonly Color Track = ToColor(FixWorldUiTheme.Track);

        private DiagnosticsDocument document = DiagnosticsDocument.Parse(
            "Status\n  No completed startup diagnostics are available yet.");
        private string diagnosticsText;
        private string ddsActionStatus =
            "Retry failed packs, or clear the cache and rebuild it next launch.";
        private float nextRefreshAt;
        private Vector2 scrollPosition;
        private int selectedSection;

        public DiagnosticsWindow()
        {
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnCancel = true;
            doCloseX = true;
            draggable = true;
            drawShadow = true;
            onlyOneOfTypeAllowed = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => DefaultSize;

        protected override float Margin => 18f;

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            windowRect.width = Mathf.Min(
                global::Verse.UI.screenWidth,
                Mathf.Max(MinimumWidth, windowRect.width));
            windowRect.height = Mathf.Min(
                global::Verse.UI.screenHeight,
                Mathf.Max(MinimumHeight, windowRect.height));
            windowRect.x = Mathf.Clamp(
                windowRect.x,
                0f,
                global::Verse.UI.screenWidth - windowRect.width);
            windowRect.y = Mathf.Clamp(
                windowRect.y,
                0f,
                global::Verse.UI.screenHeight - windowRect.height);
        }

        internal static void Toggle()
        {
            DiagnosticsWindow existing =
                Find.WindowStack.WindowOfType<DiagnosticsWindow>();
            if (existing != null)
            {
                Find.WindowStack.TryRemove(existing);
                return;
            }

            Find.WindowStack.Add(new DiagnosticsWindow());
        }

        public override void PreOpen()
        {
            base.PreOpen();
            Refresh(force: true);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Refresh(force: false);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            try
            {
                DrawHeader(inRect);

                Rect body = new Rect(
                    inRect.x,
                    inRect.y + HeaderHeight,
                    inRect.width,
                    inRect.height - HeaderHeight - FooterHeight);
                float navigationWidth = Mathf.Min(
                    NavigationWidth,
                    Mathf.Max(138f, body.width * 0.24f));
                Rect navigation = new Rect(
                    body.x,
                    body.y,
                    navigationWidth,
                    body.height);
                Rect details = new Rect(
                    navigation.xMax + NavigationGap,
                    body.y,
                    body.width - navigation.width - NavigationGap,
                    body.height);

                DrawNavigation(navigation);
                DrawSection(details);
                DrawFooter(inRect);
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                GUI.color = previousColor;
            }
        }

        private void DrawHeader(Rect bounds)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.Label(
                new Rect(bounds.x + 4f, bounds.y + 3f, 360f, 32f),
                "FixWorld diagnostics");

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = MutedText;
            Widgets.Label(
                new Rect(bounds.x + 360f, bounds.y + 9f, bounds.width - 364f, 24f),
                "Startup snapshot and DDS maintenance");

            GUI.color = Color.white;
            Widgets.DrawBoxSolid(
                new Rect(bounds.x, bounds.y + 42f, bounds.width, 3f),
                Accent);
        }

        private void DrawNavigation(Rect bounds)
        {
            GUI.color = Color.white;
            Widgets.DrawBoxSolid(bounds, Track);
            Rect inner = bounds.ContractedBy(8f);
            for (int index = 0; index < document.Sections.Count; index++)
            {
                Rect item = new Rect(
                    inner.x,
                    inner.y + index * 36f,
                    inner.width,
                    31f);
                bool selected = index == selectedSection;
                GUI.color = Color.white;
                if (selected)
                {
                    Widgets.DrawBoxSolid(item, Completed);
                    Widgets.DrawBoxSolid(
                        new Rect(item.x, item.y, 1f, item.height),
                        Accent);
                }
                else
                {
                    Widgets.DrawHighlightIfMouseover(item);
                }

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = selected ? Color.white : MutedText;
                Widgets.Label(
                    new Rect(item.x + 11f, item.y, item.width - 15f, item.height),
                    document.Sections[index].Title);
                if (Widgets.ButtonInvisible(item))
                {
                    selectedSection = index;
                    scrollPosition = Vector2.zero;
                }
            }
        }

        private void DrawSection(Rect bounds)
        {
            GUI.color = Color.white;
            Widgets.DrawBoxSolid(bounds, Track);
            Rect inner = bounds.ContractedBy(18f);
            DiagnosticsSection section = document.Sections[selectedSection];
            bool ddsSection = string.Equals(
                section.Title,
                "DDS / Textures",
                StringComparison.Ordinal);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.Label(
                new Rect(inner.x, inner.y, inner.width, 34f),
                section.Title);
            Widgets.DrawBoxSolid(
                new Rect(inner.x, inner.y + 34f, inner.width, 1f),
                Pending);

            Rect viewport = new Rect(
                inner.x,
                inner.y + 47f,
                inner.width,
                inner.height - 47f - (ddsSection ? DdsActionsHeight : 0f));
            bool stackedRows =
                string.Equals(
                    section.Title,
                    "Stages",
                    StringComparison.Ordinal);
            float rowHeight = stackedRows ? DetailedRowHeight : RowHeight;
            float contentHeight = Math.Max(
                viewport.height,
                section.Lines.Count * rowHeight);
            Rect content = new Rect(
                0f,
                0f,
                Math.Max(100f, viewport.width - 18f),
                contentHeight);
            Widgets.BeginScrollView(viewport, ref scrollPosition, content);
            for (int index = 0; index < section.Lines.Count; index++)
            {
                DrawRow(
                    new Rect(
                        content.x,
                        content.y + index * rowHeight,
                        content.width,
                        rowHeight - 3f),
                    section.Lines[index],
                    index,
                    stackedRows);
            }

            Widgets.EndScrollView();
            if (ddsSection)
            {
                DrawDdsActions(new Rect(
                    inner.x,
                    inner.yMax - DdsActionsHeight,
                    inner.width,
                    DdsActionsHeight));
            }
        }

        private void DrawDdsActions(Rect bounds)
        {
            GUI.color = Color.white;
            Widgets.DrawBoxSolid(
                new Rect(bounds.x, bounds.y, bounds.width, 1f),
                Pending);

            const float gap = 8f;
            float buttonWidth = (bounds.width - gap) / 2f;
            Rect retryButton = new Rect(
                bounds.x,
                bounds.y + 8f,
                buttonWidth,
                30f);
            Rect clearButton = new Rect(
                retryButton.xMax + gap,
                retryButton.y,
                buttonWidth,
                retryButton.height);
            if (Widgets.ButtonText(retryButton, "Retry failed DDS builds"))
            {
                RunDdsAction(() => FixWorldMod.Instance?.RetryFailedDdsBuilds() ??
                    "FixWorld.Mod is not active.");
            }

            if (Widgets.ButtonText(clearButton, "Clear DDS cache"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Delete FixWorld's DDS cache? Loaded textures remain usable " +
                    "for this session. RimWorld must be restarted to rebuild " +
                    "the cache.",
                    () => RunDdsAction(() =>
                        FixWorldMod.Instance?.ClearDdsCache() ??
                        "FixWorld.Mod is not active.")));
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            GUI.color = MutedText;
            Widgets.LabelEllipses(
                new Rect(bounds.x + 2f, bounds.y + 42f, bounds.width - 4f, 20f),
                ddsActionStatus);
            TooltipHandler.TipRegion(bounds, ddsActionStatus);
        }

        private void RunDdsAction(Func<string> action)
        {
            try
            {
                ddsActionStatus = action();
                Refresh(force: true);
            }
            catch (Exception exception)
            {
                ddsActionStatus = "DDS action failed: " + exception.Message;
                Log.Warning("[FixWorld] " + ddsActionStatus);
            }
        }

        private static void DrawRow(
            Rect bounds,
            string line,
            int index,
            bool stacked)
        {
            if (index % 2 == 0)
            {
                GUI.color = Color.white;
                Widgets.DrawBoxSolid(bounds, Row);
            }

            const string separator = ": ";
            int separatorIndex = line.IndexOf(
                separator,
                StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.LabelEllipses(bounds.ContractedBy(7f, 0f), line);
                TooltipHandler.TipRegion(bounds, line);
                return;
            }

            string label = line.Substring(0, separatorIndex);
            string value = line.Substring(separatorIndex + separator.Length);
            if (stacked)
            {
                DrawStackedRow(bounds, label, value, line);
                return;
            }

            float labelWidth = bounds.width * 0.43f;
            Rect labelBounds = new Rect(
                bounds.x + 7f,
                bounds.y,
                labelWidth - 12f,
                bounds.height);
            Rect valueBounds = new Rect(
                bounds.x + labelWidth,
                bounds.y,
                bounds.width - labelWidth - 7f,
                bounds.height);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = MutedText;
            Widgets.LabelEllipses(labelBounds, label);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.LabelEllipses(valueBounds, value);
            TooltipHandler.TipRegion(bounds, line);
        }

        private static void DrawStackedRow(
            Rect bounds,
            string label,
            string value,
            string tooltip)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = MutedText;
            Widgets.LabelEllipses(
                new Rect(
                    bounds.x + 7f,
                    bounds.y + 2f,
                    bounds.width - 14f,
                    18f),
                label);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.LabelEllipses(
                new Rect(
                    bounds.x + 7f,
                    bounds.y + 20f,
                    bounds.width - 14f,
                    21f),
                value);
            TooltipHandler.TipRegion(bounds, tooltip);
        }

        private static void DrawFooter(Rect bounds)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            GUI.color = MutedText;
            Widgets.Label(
                new Rect(
                    bounds.x + 4f,
                    bounds.yMax - FooterHeight,
                    bounds.width - 8f,
                    FooterHeight),
                "Runtime snapshot   /   refreshes at most every 500 ms");
        }

        private void Refresh(bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && now < nextRefreshAt)
            {
                return;
            }

            nextRefreshAt = now + RefreshIntervalSeconds;
            string current = FixWorldMod.Instance?.GetDiagnosticsText() ??
                "Status\n  FixWorld.Mod is not active.";
            if (string.Equals(
                    current,
                    diagnosticsText,
                    StringComparison.Ordinal))
            {
                return;
            }

            string selectedTitle = document.Sections[selectedSection].Title;
            diagnosticsText = current;
            document = DiagnosticsDocument.Parse(current);
            selectedSection = document.FindSection(selectedTitle);
            scrollPosition = Vector2.zero;
        }

        private static Color ToColor(UiColor color)
        {
            return new Color(color.Red, color.Green, color.Blue, color.Alpha);
        }
    }

    public sealed class MainButtonWorker_Diagnostics : MainButtonWorker
    {
        public override void Activate()
        {
            DiagnosticsWindow.Toggle();
        }
    }
}
