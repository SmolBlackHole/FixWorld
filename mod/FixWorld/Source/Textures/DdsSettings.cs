using System;
using System.Globalization;
using FixWorld.Settings;

namespace FixWorld.Textures
{
    internal sealed class DdsSettings : IDisposable
    {
        internal const int DefaultMaximumGiB = 6;
        internal const int DefaultMinimumFreeGiB = 10;
        internal const string MaximumEnvironmentVariable = "FIXWORLD_DDS_CACHE_MAX_GIB";
        internal const string MinimumFreeEnvironmentVariable = "FIXWORLD_DDS_CACHE_MIN_FREE_GIB";
        private readonly Action changed;

        internal DdsSettings(ModSettingsPack pack, Action changed)
        {
            this.changed = changed;
            Pack = pack;
            MaximumGiB = pack.GetHandle("ddsMaximumGiB", "DDS cache limit (GiB)",
                "Maximum disk space used by DDS packs. Free-drive reserve takes priority. Zero clears and disables new cache writes.",
                DefaultMaximumGiB, Validators.IntRangeValidator(0, 1048576));
            MinimumFreeGiB = pack.GetHandle("ddsMinimumFreeGiB", "DDS free-drive reserve (GiB)",
                "DDS removes its own packs when needed to preserve this much free drive space, even below the cache limit.",
                DefaultMinimumFreeGiB, Validators.IntRangeValidator(0, 1048576));
            MaximumGiB.DisplayOrder = 100;
            MinimumFreeGiB.DisplayOrder = 101;
            MaximumGiB.ValueChanged += OnChanged;
            MinimumFreeGiB.ValueChanged += OnChanged;
        }

        internal ModSettingsPack Pack { get; }
        internal SettingHandle<int> MaximumGiB { get; }
        internal SettingHandle<int> MinimumFreeGiB { get; }
        internal long EffectiveMaximumBytes => ReadOverride(MaximumEnvironmentVariable, MaximumGiB.Value);
        internal long EffectiveMinimumFreeBytes => ReadOverride(MinimumFreeEnvironmentVariable, MinimumFreeGiB.Value);
        internal bool MaximumOverridden => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MaximumEnvironmentVariable));
        internal bool MinimumFreeOverridden => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MinimumFreeEnvironmentVariable));
        internal bool Owns(SettingHandle handle) => ReferenceEquals(handle, MaximumGiB) || ReferenceEquals(handle, MinimumFreeGiB);

        private void OnChanged(SettingHandle handle) => changed();

        internal static long ReadOverride(string name, int fallbackGiB)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                return checked((long)fallbackGiB * DdsBudget.GiB);
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gib) ||
                double.IsNaN(gib) || double.IsInfinity(gib) || gib < 0 || gib >= long.MaxValue / (double)DdsBudget.GiB)
                throw new InvalidOperationException("Invalid non-negative GiB value in " + name + ": " + value);
            return (long)Math.Floor(gib * DdsBudget.GiB);
        }

        public void Dispose()
        {
            MaximumGiB.ValueChanged -= OnChanged;
            MinimumFreeGiB.ValueChanged -= OnChanged;
        }
    }
}
