using System;
using System.Diagnostics;
using System.Threading;

namespace FixWorld.Profiling
{
    public sealed class ProfileScope<TKey> : IDisposable
    {
        private readonly TKey key;
        private readonly Profiler<TKey> profiler;
        private readonly long startedAt;
        private int completed;

        internal ProfileScope(Profiler<TKey> profiler, TKey key)
        {
            this.profiler = profiler;
            this.key = key;
            startedAt = Stopwatch.GetTimestamp();
        }

        public void Complete()
        {
            Finish(succeeded: true);
        }

        public void Fail()
        {
            Finish(succeeded: false);
        }

        public void Dispose()
        {
            Complete();
        }

        private void Finish(bool succeeded)
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
            {
                return;
            }

            long elapsedTicks = Math.Max(
                0L,
                Stopwatch.GetTimestamp() - startedAt);
            profiler.Observe(
                key,
                TimeSpan.FromSeconds(
                    (double)elapsedTicks / Stopwatch.Frequency),
                succeeded);
        }
    }
}
