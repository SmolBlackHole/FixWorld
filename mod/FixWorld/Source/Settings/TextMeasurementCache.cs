// SPDX-License-Identifier: MPL-2.0
using System;
using FixWorld.Caching;
using Verse;

namespace FixWorld.Settings
{
    // Engine adapter only. Storage, bounds, diagnostics and invalidation live in
    // the shared cache. Keys contain values, never live mutable SettingHandles.
    internal sealed class TextMeasurementCache
    {
        private readonly TypedCache<HeightKey, float> heights;
        public TextMeasurementCache(CacheStore caches)
        {
            heights = caches.Create(new CacheContract<HeightKey, float>("settings.text_height", 512, CalculateHeight));
        }
        public float Height(string text, float width, GameFont font)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (float.IsNaN(width) || float.IsInfinity(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            return heights.GetOrAdd(new HeightKey(text, width, font, Prefs.UIScale, Prefs.LangFolderName));
        }
        private static float CalculateHeight(HeightKey key)
        {
            var previous = Text.Font;
            try { Text.Font = key.Font; return Text.CalcHeight(key.Text, key.Width); }
            finally { Text.Font = previous; }
        }
        private readonly struct HeightKey : IEquatable<HeightKey>
        {
            public HeightKey(string text, float width, GameFont font, float scale, string language)
            { Text = text; Width = width; Font = font; Scale = scale; Language = language; }
            public string Text { get; }
            public float Width { get; }
            public GameFont Font { get; }
            private float Scale { get; }
            private string Language { get; }
            public bool Equals(HeightKey other) => Text == other.Text && Width == other.Width && Font == other.Font && Scale == other.Scale && Language == other.Language;
            public override bool Equals(object obj) => obj is HeightKey other && Equals(other);
            public override int GetHashCode() => unchecked((((Text.GetHashCode() * 397 ^ Width.GetHashCode()) * 397 ^ (int)Font) * 397 ^ Scale.GetHashCode()) * 397 ^ (Language?.GetHashCode() ?? 0));
        }
    }
}
