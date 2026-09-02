namespace FixWorld.Presentation
{
    internal readonly struct UiColor
    {
        internal UiColor(float red, float green, float blue, float alpha)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        internal float Red { get; }

        internal float Green { get; }

        internal float Blue { get; }

        internal float Alpha { get; }
    }

    internal static class FixWorldUiTheme
    {
        internal static readonly UiColor Accent =
            new UiColor(0.25f, 0.73f, 0.90f, 1f);
        internal static readonly UiColor Completed =
            new UiColor(0.16f, 0.48f, 0.68f, 1f);
        internal static readonly UiColor Pending =
            new UiColor(1f, 1f, 1f, 0.14f);
        internal static readonly UiColor Track =
            new UiColor(0f, 0f, 0f, 0.34f);
        internal static readonly UiColor Row =
            new UiColor(1f, 1f, 1f, 0.035f);
        internal static readonly UiColor MutedText =
            new UiColor(1f, 1f, 1f, 0.58f);
    }
}
