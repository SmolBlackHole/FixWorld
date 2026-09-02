using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace FixWorld.UI
{
    public sealed class DiagnosticsWindow : Window
    {
        private const float RefreshIntervalSeconds = 0.5f;
        private static readonly Vector2 DefaultSize = new Vector2(900f, 650f);

        private string diagnosticsText =
            "No completed startup diagnostics are available yet.";
        private float measuredContentHeight;
        private float measuredContentWidth = -1f;
        private float nextRefreshAt;
        private Vector2 scrollPosition;

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
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(
                    new Rect(inRect.x, inRect.y, inRect.width, 32f),
                    "FixWorld diagnostics");

                Text.Font = GameFont.Small;
                Rect body = new Rect(
                    inRect.x,
                    inRect.y + 40f,
                    inRect.width,
                    inRect.height - 40f);
                float contentWidth = Math.Max(100f, body.width - 20f);
                if (Math.Abs(contentWidth - measuredContentWidth) > 0.5f)
                {
                    measuredContentWidth = contentWidth;
                    measuredContentHeight =
                        Text.CalcHeight(diagnosticsText, contentWidth) + 8f;
                }

                Rect content = new Rect(
                    0f,
                    0f,
                    contentWidth,
                    Math.Max(body.height, measuredContentHeight));
                Widgets.BeginScrollView(body, ref scrollPosition, content);
                Widgets.Label(content, diagnosticsText);
                Widgets.EndScrollView();
            }
            finally
            {
                Text.Font = previousFont;
            }
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
                "FixWorld.Mod is not active.";
            if (!string.Equals(
                    current,
                    diagnosticsText,
                    StringComparison.Ordinal))
            {
                diagnosticsText = current;
                measuredContentWidth = -1f;
            }
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
