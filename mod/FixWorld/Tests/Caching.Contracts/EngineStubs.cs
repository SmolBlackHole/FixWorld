// SPDX-License-Identifier: MPL-2.0
// Deterministic engine boundary for the real TextMeasurementCache adapter.
namespace Verse
{
    public enum GameFont { Tiny, Small, Medium }
    public static class Prefs
    {
        public static float UIScale { get; set; } = 1;
        public static string LangFolderName { get; set; } = "English";
    }
    public static class Text
    {
        public static GameFont Font { get; set; } = GameFont.Medium;
        public static int Calls { get; private set; }
        public static bool Throw { get; set; }
        public static float CalcHeight(string text, float width)
        {
            Calls++;
            if (Throw) throw new System.InvalidOperationException("Engine measurement failed");
            return text.Length + width * 0.01f + (int)Font;
        }
    }
}
