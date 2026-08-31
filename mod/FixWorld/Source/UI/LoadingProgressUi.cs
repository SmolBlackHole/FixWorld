using System;
using System.Globalization;
using FixWorld.Loading;
using UnityEngine;
using UnityEngine.Profiling;
using Verse;

namespace FixWorld.UI
{
    internal static class LoadingProgressUi
    {
        private const float PanelHeight = 254f;
        private const float PanelMaxWidth = 860f;

        private static readonly Color Accent = new Color(0.25f, 0.73f, 0.90f, 1f);
        private static readonly Color Completed = new Color(0.16f, 0.48f, 0.68f, 1f);
        private static readonly Color Pending = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color Track = new Color(0f, 0f, 0f, 0.34f);

        private static float nextMemoryRefresh;
        private static long managedBytes;
        private static long unityBytes;
        private static SystemMemorySnapshot systemMemory;

        internal static void Draw(LoadingSnapshot snapshot)
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
                DrawStages(content, snapshot.Stage);
                DrawFooter(content, snapshot.HasDurationEstimate);
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                GUI.color = previousColor;
            }
        }

        private static void DrawHeader(Rect content, LoadingSnapshot snapshot)
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

        private static void DrawCurrentStep(Rect content, LoadingSnapshot snapshot)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(
                new Rect(content.x, content.y + 38f, 300f, 24f),
                (int)snapshot.Stage + " / 5   " + snapshot.StageName);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.LabelEllipses(
                new Rect(content.x, content.y + 62f, content.width, 24f),
                snapshot.StepName);

            if (!string.IsNullOrEmpty(snapshot.Activity) &&
                !string.Equals(snapshot.Activity, snapshot.StepName, StringComparison.Ordinal))
            {
                Text.Font = GameFont.Tiny;
                Widgets.LabelEllipses(
                    new Rect(content.x, content.y + 86f, content.width, 20f),
                    snapshot.Activity);
            }
        }

        private static void DrawProgressBars(Rect content, LoadingSnapshot snapshot)
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

        private static void DrawStages(Rect content, LoadingStage stage)
        {
            const float gap = 5f;
            Rect rail = new Rect(content.x, content.y + 159f, content.width, 7f);
            float segmentWidth = (rail.width - gap * 4f) / 5f;
            for (int number = 1; number <= 5; number++)
            {
                Color color = number < (int)stage
                    ? Completed
                    : number == (int)stage
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
                    GetShortStageName(number));
            }
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

        private static string GetShortStageName(int number)
        {
            switch (number)
            {
                case 1: return "Bootstrap";
                case 2: return "XML";
                case 3: return "Definitions";
                case 4: return "Content";
                case 5: return "Finalize";
                default: throw new ArgumentOutOfRangeException(nameof(number), number, null);
            }
        }
    }
}
