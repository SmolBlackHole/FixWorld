using FixWorld.Loading;
using UnityEngine;
using Verse;

namespace FixWorld.UI
{
    internal static class LoadingProgressUi
    {
        private static readonly Color CompletedColor = new Color(0.20f, 0.55f, 0.82f, 1f);
        private static readonly Color CurrentColor = new Color(0.35f, 0.78f, 1f, 1f);
        private static readonly Color PendingColor = new Color(1f, 1f, 1f, 0.16f);

        internal static void Draw(Rect rect, LoadingSnapshot snapshot)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            try
            {
                Rect content = rect.ContractedBy(14f, 7f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(
                    new Rect(content.x, content.y, content.width, 22f),
                    "FixWorld loading  " + (int)snapshot.Stage + "/5  " + snapshot.StageName);

                Text.Font = GameFont.Tiny;
                Widgets.LabelEllipses(
                    new Rect(content.x, content.y + 21f, content.width, 19f),
                    snapshot.StepName);

                const float gap = 4f;
                Rect bar = new Rect(content.x, content.yMax - 9f, content.width, 7f);
                float segmentWidth = (bar.width - gap * 4f) / 5f;
                for (int number = 1; number <= 5; number++)
                {
                    Color color = number < (int)snapshot.Stage
                        ? CompletedColor
                        : number == (int)snapshot.Stage
                            ? CurrentColor
                            : PendingColor;
                    Widgets.DrawBoxSolid(
                        new Rect(
                            bar.x + (number - 1) * (segmentWidth + gap),
                            bar.y,
                            segmentWidth,
                            bar.height),
                        color);
                }
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
            }
        }
    }
}
