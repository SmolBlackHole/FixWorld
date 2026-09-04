using System.Runtime.CompilerServices;

namespace FixWorld.Profiling
{
    public ref struct ProfileScope<TKey>
    {
        private readonly ProfileSlot<TKey> slot;
        private long startedAt;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ProfileScope(ProfileSlot<TKey> slot)
        {
            this.slot = slot;
            startedAt = slot.StartTimestamp();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete() => Finish(succeeded: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Fail() => Finish(succeeded: false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => Complete();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Finish(bool succeeded)
        {
            long current = startedAt;
            if (current == ProfileSlot<TKey>.InactiveTimestamp)
            {
                return;
            }

            startedAt = ProfileSlot<TKey>.InactiveTimestamp;
            slot.StopTimestamp(current, succeeded);
        }
    }
}
