using System;

namespace FixWorld.Profiling
{
    /// <summary>
    /// Main-thread-owned measurement of completed ticks over stopwatch time.
    /// </summary>
    public sealed class TickRateCounter
    {
        private readonly long sampleIntervalStopwatchTicks;
        private long windowStartedAt;
        private bool started;

        public TickRateCounter(long stopwatchFrequency)
        {
            if (stopwatchFrequency <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stopwatchFrequency),
                    stopwatchFrequency,
                    "The stopwatch frequency must be positive.");
            }

            StopwatchFrequency = stopwatchFrequency;
            sampleIntervalStopwatchTicks = stopwatchFrequency;
        }

        public long StopwatchFrequency { get; }

        public double TPS { get; private set; }

        public long WindowTickCount { get; private set; }

        public long WindowElapsedStopwatchTicks { get; private set; }

        public bool IsPaused { get; private set; }

        public void Reset(long timestamp)
        {
            windowStartedAt = timestamp;
            WindowTickCount = 0L;
            WindowElapsedStopwatchTicks = 0L;
            TPS = 0.0;
            started = true;
            IsPaused = false;
        }

        public void RecordTick()
        {
            if (started && !IsPaused)
            {
                WindowTickCount++;
            }
        }

        public void Update(long timestamp, bool paused)
        {
            if (paused)
            {
                ClearWindow(timestamp);
                IsPaused = true;
                return;
            }

            if (!started || IsPaused || timestamp < windowStartedAt)
            {
                StartWindow(timestamp);
                return;
            }

            WindowElapsedStopwatchTicks = timestamp - windowStartedAt;
            if (WindowElapsedStopwatchTicks < sampleIntervalStopwatchTicks)
            {
                return;
            }

            TPS = WindowElapsedStopwatchTicks == 0L
                ? 0.0
                : WindowTickCount * (double)StopwatchFrequency /
                  WindowElapsedStopwatchTicks;
            StartWindow(timestamp);
        }

        private void StartWindow(long timestamp)
        {
            windowStartedAt = timestamp;
            WindowTickCount = 0L;
            WindowElapsedStopwatchTicks = 0L;
            started = true;
            IsPaused = false;
        }

        private void ClearWindow(long timestamp)
        {
            windowStartedAt = timestamp;
            WindowTickCount = 0L;
            WindowElapsedStopwatchTicks = 0L;
            TPS = 0.0;
            started = true;
        }
    }
}
