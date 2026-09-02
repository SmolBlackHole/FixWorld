using System;

namespace FixWorld.PlayData
{
    internal sealed class PlayDataOperationEvent
    {
        internal PlayDataOperationEvent(
            PlayDataLoadStage stage,
            string name,
            TimeSpan elapsed,
            bool succeeded)
        {
            Stage = stage;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Elapsed = elapsed;
            Succeeded = succeeded;
        }

        internal PlayDataLoadStage Stage { get; }

        internal string Name { get; }

        internal TimeSpan Elapsed { get; }

        internal bool Succeeded { get; }
    }
}
