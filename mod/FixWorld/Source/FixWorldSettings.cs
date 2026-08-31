using Verse;

namespace FixWorld
{
    public sealed class FixWorldSettings : ModSettings
    {
        internal bool PreloaderPromptDismissed;

        public override void ExposeData()
        {
            Scribe_Values.Look(
                ref PreloaderPromptDismissed,
                "preloaderPromptDismissed",
                false);
        }
    }
}
