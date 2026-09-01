using System;
using Verse;

namespace FixWorld
{
    public sealed class FixWorldSettings : ModSettings
    {
        internal float DdsCacheMaxGiB = 6.0f;

        public override void ExposeData()
        {
            Scribe_Values.Look(
                ref DdsCacheMaxGiB,
                "ddsCacheMaxGiB",
                6.0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                DdsCacheMaxGiB = Math.Max(1.0f, Math.Min(64.0f, DdsCacheMaxGiB));
            }
        }
    }
}
