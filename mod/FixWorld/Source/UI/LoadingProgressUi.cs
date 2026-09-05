// SPDX-License-Identifier: MPL-2.0
using System;
using System.Globalization;
using UnityEngine;
using Verse;
using static FixWorld.UI.UiTheme;

namespace FixWorld.UI
{
    // Restore the incumbent centered dark panel, native text and cyan stage rails.
    // Operate: make the current stage legible; retain the dry tips without a side stripe.
    internal static class LoadingProgressUi
    {
        private static float nextMemoryRefresh;
        private static string memory = "";
        internal static void Draw(LoadingProgress loading)
        {
            LoadingSnapshot data = loading?.Current;
            if (data?.Active != true)
            {
                return;
            }

            double elapsed = loading.Elapsed(data);
            float width = Mathf.Min(860f, Verse.UI.screenWidth - 32f);
            float height = Mathf.Min(326f, Verse.UI.screenHeight - 32f);
            Rect panel = new Rect((Verse.UI.screenWidth - width) / 2f, (Verse.UI.screenHeight - height) / 2f, width, height).Rounded();
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            try
            {
                Widgets.DrawWindowBackground(panel);
                Rect area = panel.ContractedBy(24f, 18f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(area.x, area.y, area.width * .6f, 30f), "FixWorld loading");
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(area.x, area.y + 2f, area.width, 26f), "Elapsed " + TimeSpan.FromMilliseconds(elapsed).ToString(@"mm\:ss", CultureInfo.InvariantCulture));
                int stage = (int)data.Stage, group = LoadingProgress.Group(data.Stage);
                int first = LoadingProgress.GroupStarts[group], count = LoadingProgress.GroupStarts[group + 1] - first;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(area.x, area.y + 38f, area.width, 24f),
                    $"{LoadingProgress.Groups[group]} {stage - first + 1} / {count}   |   Overall {stage + 1} / {LoadingProgress.Names.Length}");
                Widgets.LabelEllipses(new Rect(area.x, area.y + 64f, area.width, 25f), LoadingProgress.Names[stage]);
                var busy = new Rect(area.x, area.y + 110f, area.width, 14f);
                Widgets.DrawBoxSolid(busy, new Color(0, 0, 0, .34f));
                float segment = Mathf.Min(120f, busy.width * .18f);
                float start = Mathf.Repeat((float)(elapsed / 1400), 1) * (busy.width + segment) - segment;
                float left = Mathf.Max(0, start), right = Mathf.Min(busy.width, start + segment);
                if (right > left)
                {
                    Widgets.DrawBoxSolid(new Rect(busy.x + left, busy.y, right - left, busy.height), Completed);
                }

                Widgets.DrawBox(busy);
                var overall = new Rect(area.x, area.y + 133f, area.width, 16f);
                Widgets.DrawBoxSolid(overall, new Color(0, 0, 0, .34f));
                Widgets.DrawBoxSolid(new Rect(overall.x, overall.y, overall.width * stage / (LoadingProgress.Names.Length - 1), overall.height), Accent);
                Widgets.DrawBox(overall);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = stage >= LoadingProgress.Names.Length / 2 ? new Color(.05f, .08f, .09f) : Color.white;
                Widgets.Label(overall, "Stage progress");
                GUI.color = color;
                Rail(area, 156f, LoadingProgress.Groups, 0, 4, group);
                Rail(area, 187f, LoadingProgress.ShortNames, first, count, stage);
                var tip = new Rect(area.x, area.y + 220f, area.width, 42f);
                Widgets.DrawBoxSolid(tip, Row);
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Accent;
                Widgets.Label(new Rect(tip.x + 12f, tip.y, 32f, tip.height), "Tip");
                GUI.color = color;
                // Two lines, not an ellipsis that hides the punchline.
                Widgets.Label(new Rect(tip.x + 52f, tip.y, tip.width - 64f, tip.height), LoadingTips.Get(elapsed));
                if (Time.realtimeSinceStartup >= nextMemoryRefresh)
                {
                    nextMemoryRefresh = Time.realtimeSinceStartup + .5f;
                    memory = $"Heap {GC.GetTotalMemory(false) / 1048576:N0} MiB   /   Unity {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576:N0} MiB";
                }
                Text.Anchor = TextAnchor.LowerLeft;
                Widgets.Label(new Rect(area.x, area.yMax - 22f, area.width, 20f), memory);
            }
            finally { Text.Font = font; Text.Anchor = anchor; GUI.color = color; }
        }
        private static void Rail(Rect area, float y, string[] names, int first, int count, int active)
        {
            float width = (area.width - 5f * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                int item = first + i;
                var rect = new Rect(area.x + i * (width + 5f), area.y + y, width, 6f);
                Widgets.DrawBoxSolid(rect, item < active ? Completed : item == active ? Accent : Pending);
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(rect.x, rect.yMax + 3f, width, 20f), names[item]);
            }
        }
    }
}
