using System;
using System.Globalization;
using FixWorld.Diagnostics;
using FixWorld.PlayData;
using FixWorld.Presentation;
using UnityEngine;
using UnityEngine.Profiling;
using Verse;

namespace FixWorld.UI
{
    internal static class LoadingProgressUi
    {
        private const float PanelHeight = 326f;
        private const float PanelMaxWidth = 860f;

        private static readonly Color Accent = ToColor(FixWorldUiTheme.Accent);
        private static readonly Color Completed = ToColor(FixWorldUiTheme.Completed);
        private static readonly Color Pending = ToColor(FixWorldUiTheme.Pending);
        private static readonly Color Row = ToColor(FixWorldUiTheme.Row);
        private static readonly Color Track = ToColor(FixWorldUiTheme.Track);

        private static float nextMemoryRefresh;
        private static long managedBytes;
        private static long unityBytes;
        private static SystemMemorySnapshot systemMemory;

        internal static void Draw(PlayDataLoadingSnapshot snapshot)
        {
            RefreshMemoryMetrics();

            float width = Mathf.Min(PanelMaxWidth, global::Verse.UI.screenWidth - 64f);
            Rect panel = new Rect(
                (global::Verse.UI.screenWidth - width) / 2f,
                (global::Verse.UI.screenHeight - PanelHeight) / 2f,
                width,
                PanelHeight).Rounded();
            Widgets.DrawShadowAround(panel);
            Widgets.DrawWindowBackground(panel);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            try
            {
                Rect content = panel.ContractedBy(24f, 18f);
                DrawHeader(content, snapshot);
                DrawCurrentStep(content, snapshot);
                DrawProgressBars(content, snapshot);
                DrawStageGroups(content, snapshot.Stage);
                DrawActiveGroupStages(content, snapshot.Stage);
                DrawLoadingTip(content, snapshot.ElapsedMilliseconds);
                DrawFooter(content, snapshot.HasDurationEstimate);
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                GUI.color = previousColor;
            }
        }

        private static void DrawHeader(
            Rect content,
            PlayDataLoadingSnapshot snapshot)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(content.x, content.y, 300f, 30f),
                "FixWorld loading");

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            string timing = "Elapsed " + FormatDuration(snapshot.ElapsedMilliseconds);
            if (snapshot.HasDurationEstimate)
            {
                timing += "   /   ~" + FormatDuration(snapshot.EstimatedTotalMilliseconds);
            }

            Widgets.Label(
                new Rect(content.x + 300f, content.y + 2f, content.width - 300f, 26f),
                timing);
        }

        private static void DrawCurrentStep(
            Rect content,
            PlayDataLoadingSnapshot snapshot)
        {
            string stageName = PlayDataLoadStageCatalog.GetName(snapshot.Stage);
            PlayDataLoadStageGroup group =
                PlayDataLoadStageCatalog.GetGroup(snapshot.Stage);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(content.x, content.y + 38f, 440f, 24f),
                PlayDataLoadStageCatalog.GetGroupName(group) + " " +
                PlayDataLoadStageCatalog.GetIndexInGroup(snapshot.Stage) +
                " / " + PlayDataLoadStageCatalog.GetGroupStageCount(group) +
                "   |   Overall " + (int)snapshot.Stage + " / " +
                PlayDataLoadStageCatalog.Count);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(
                new Rect(content.x + 440f, content.y + 40f, content.width - 440f, 20f),
                "Observed RimWorld play-data load");

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.LabelEllipses(
                new Rect(content.x, content.y + 62f, content.width, 24f),
                stageName);
        }

        private static void DrawProgressBars(
            Rect content,
            PlayDataLoadingSnapshot snapshot)
        {
            Rect currentBar = new Rect(content.x, content.y + 110f, content.width, 14f);
            Widgets.DrawBoxSolid(currentBar, Track);
            DrawIndeterminateFill(currentBar, snapshot.ElapsedMilliseconds);

            Widgets.DrawBox(currentBar);

            Rect overallBar = new Rect(content.x, content.y + 133f, content.width, 16f);
            Widgets.DrawBoxSolid(overallBar, Track);
            DrawFill(overallBar, snapshot.Progress);
            Widgets.DrawBox(overallBar);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            string label = snapshot.HasDurationEstimate
                ? Mathf.RoundToInt(snapshot.Progress * 100f) + "%"
                : "Stage progress";
            Color previousColor = GUI.color;
            GUI.color = snapshot.Progress >= 0.52f
                ? new Color(0.05f, 0.08f, 0.09f, 1f)
                : Color.white;
            Widgets.Label(overallBar, label);
            GUI.color = previousColor;
        }

        private static void DrawStageGroups(
            Rect content,
            PlayDataLoadStage stage)
        {
            const float gap = 5f;
            Rect rail = new Rect(content.x, content.y + 156f, content.width, 7f);
            float segmentWidth =
                (rail.width - gap * (PlayDataLoadStageCatalog.GroupCount - 1)) /
                PlayDataLoadStageCatalog.GroupCount;
            PlayDataLoadStageGroup activeGroup =
                PlayDataLoadStageCatalog.GetGroup(stage);
            for (int number = 1;
                 number <= PlayDataLoadStageCatalog.GroupCount;
                 number++)
            {
                PlayDataLoadStageGroup group = (PlayDataLoadStageGroup)number;
                Color color = number < (int)activeGroup
                    ? Completed
                    : number == (int)activeGroup
                        ? Accent
                        : Pending;
                float x = rail.x + (number - 1) * (segmentWidth + gap);
                Widgets.DrawBoxSolid(
                    new Rect(x, rail.y, segmentWidth, rail.height),
                    color);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(
                    new Rect(x, rail.yMax + 4f, segmentWidth, 19f),
                    PlayDataLoadStageCatalog.GetGroupName(group));
            }
        }

        private static void DrawActiveGroupStages(
            Rect content,
            PlayDataLoadStage stage)
        {
            const float gap = 5f;
            PlayDataLoadStageGroup group =
                PlayDataLoadStageCatalog.GetGroup(stage);
            PlayDataLoadStage first =
                PlayDataLoadStageCatalog.GetFirstStage(group);
            int count = PlayDataLoadStageCatalog.GetGroupStageCount(group);
            Rect rail = new Rect(content.x, content.y + 187f, content.width, 6f);
            float segmentWidth = (rail.width - gap * (count - 1)) / count;
            for (int index = 0; index < count; index++)
            {
                PlayDataLoadStage item =
                    (PlayDataLoadStage)((int)first + index);
                Color color = item < stage
                    ? Completed
                    : item == stage
                        ? Accent
                        : Pending;
                float x = rail.x + index * (segmentWidth + gap);
                Widgets.DrawBoxSolid(
                    new Rect(x, rail.y, segmentWidth, rail.height),
                    color);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(
                    new Rect(x, rail.yMax + 3f, segmentWidth, 19f),
                    PlayDataLoadStageCatalog.GetShortName(item));
            }
        }

        private static void DrawLoadingTip(
            Rect content,
            double elapsedMilliseconds)
        {
            Rect bounds = new Rect(
                content.x,
                content.y + 220f,
                content.width,
                42f);
            Widgets.DrawBoxSolid(bounds, Row);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Color previousColor = GUI.color;
            GUI.color = Accent;
            Widgets.Label(
                new Rect(bounds.x + 12f, bounds.y, 82f, bounds.height),
                "LOADING TIP");
            GUI.color = previousColor;
            Widgets.LabelEllipses(
                new Rect(
                    bounds.x + 96f,
                    bounds.y,
                    bounds.width - 108f,
                    bounds.height),
                LoadingTips.Get(elapsedMilliseconds));
        }

        private static void DrawFooter(Rect content, bool hasDurationEstimate)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            string metrics = "Heap " + FormatBytes(managedBytes) +
                             "   /   Unity " + FormatBytes(unityBytes);
            if (systemMemory.Available)
            {
                metrics += "   /   Process " + FormatBytes(systemMemory.ProcessBytes) +
                           "   /   Free RAM " + FormatBytes(systemMemory.FreePhysicalBytes);
            }

            Widgets.Label(
                new Rect(content.x, content.yMax - 22f, content.width * 0.72f, 20f),
                metrics);

            if (hasDurationEstimate)
            {
                Text.Anchor = TextAnchor.LowerRight;
                Widgets.Label(
                    new Rect(
                        content.x + content.width * 0.70f,
                        content.yMax - 22f,
                        content.width * 0.30f,
                        20f),
                    "Estimate from previous launch");
            }
        }

        private static void DrawFill(Rect bar, float progress)
        {
            float width = Mathf.Clamp01(progress) * bar.width;
            if (width > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, width, bar.height), Accent);
            }
        }

        private static void DrawIndeterminateFill(Rect bar, double elapsedMilliseconds)
        {
            float width = Mathf.Min(120f, bar.width * 0.18f);
            float travel = bar.width + width;
            float start = bar.x - width +
                          Mathf.Repeat((float)elapsedMilliseconds / 1400f, 1f) * travel;
            float clippedStart = Mathf.Max(start, bar.x);
            float clippedEnd = Mathf.Min(start + width, bar.xMax);
            if (clippedEnd > clippedStart)
            {
                Widgets.DrawBoxSolid(
                    new Rect(clippedStart, bar.y, clippedEnd - clippedStart, bar.height),
                    Completed);
            }
        }

        private static void RefreshMemoryMetrics()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextMemoryRefresh)
            {
                return;
            }

            nextMemoryRefresh = now + 0.5f;
            managedBytes = GC.GetTotalMemory(false);
            unityBytes = Profiler.GetTotalReservedMemoryLong();
            systemMemory = SystemMemoryMetrics.Read();
        }

        private static string FormatDuration(double milliseconds)
        {
            TimeSpan duration = TimeSpan.FromMilliseconds(milliseconds);
            return ((int)duration.TotalMinutes).ToString("00", CultureInfo.InvariantCulture) +
                   ":" + duration.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string FormatBytes(long bytes)
        {
            const double gibibyte = 1024.0 * 1024.0 * 1024.0;
            if (bytes >= gibibyte)
            {
                return (bytes / gibibyte).ToString("0.0", CultureInfo.InvariantCulture) + " GiB";
            }

            return (bytes / (1024.0 * 1024.0))
                .ToString("N0", CultureInfo.InvariantCulture) + " MiB";
        }

        private static Color ToColor(UiColor color)
        {
            return new Color(color.Red, color.Green, color.Blue, color.Alpha);
        }

    }
}
