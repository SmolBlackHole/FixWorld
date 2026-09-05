using System;
using FixWorld.Profiling;

internal static class TickRateContracts
{
    internal static void Run(Action<bool, string> assert)
    {
        if (assert == null)
        {
            throw new ArgumentNullException(nameof(assert));
        }

        RejectsInvalidFrequency(assert);
        SamplesCompletedTicksAgainstAbsoluteTime(assert);
        HoldsAnIncompleteSample(assert);
        PausingClearsAndResumingStartsFresh(assert);
        HandlesZeroElapsedAndBackwardTime(assert);
        ResetAcceptsAnAbsoluteTimestamp(assert);
    }

    private static void RejectsInvalidFrequency(Action<bool, string> assert)
    {
        bool threw = false;
        try
        {
            new TickRateCounter(0L);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        assert(threw, "A non-positive stopwatch frequency was accepted.");
    }

    private static void SamplesCompletedTicksAgainstAbsoluteTime(
        Action<bool, string> assert)
    {
        TickRateCounter counter = new(100L);
        counter.Reset(1_000_000L);
        for (int tick = 0; tick < 60; tick++)
        {
            counter.RecordTick();
        }

        counter.Update(1_000_100L, paused: false);

        assert(
            counter.TPS == 60.0,
            "The tick rate did not use completed ticks over stopwatch time.");
        assert(
            counter.WindowTickCount == 0L &&
            counter.WindowElapsedStopwatchTicks == 0L,
            "A published sample did not start a fresh measurement window.");
    }

    private static void HoldsAnIncompleteSample(Action<bool, string> assert)
    {
        TickRateCounter counter = new(100L);
        counter.Reset(5_000L);
        counter.RecordTick();
        counter.RecordTick();
        counter.RecordTick();
        counter.Update(5_050L, paused: false);

        assert(counter.TPS == 0.0, "An incomplete sample was published early.");
        assert(
            counter.WindowTickCount == 3L &&
            counter.WindowElapsedStopwatchTicks == 50L,
            "An incomplete sample lost its count or elapsed time.");

        counter.Update(5_100L, paused: false);
        assert(counter.TPS == 3.0, "The completed sample rate was incorrect.");
    }

    private static void PausingClearsAndResumingStartsFresh(
        Action<bool, string> assert)
    {
        TickRateCounter counter = new(100L);
        counter.Reset(100L);
        counter.RecordTick();
        counter.RecordTick();
        counter.Update(200L, paused: true);

        assert(
            counter.IsPaused &&
            counter.TPS == 0.0 &&
            counter.WindowTickCount == 0L &&
            counter.WindowElapsedStopwatchTicks == 0L,
            "Pausing did not clear the published rate and window.");

        counter.RecordTick();
        counter.Update(2_000L, paused: false);
        counter.RecordTick();
        counter.RecordTick();
        counter.RecordTick();
        counter.RecordTick();
        counter.Update(2_100L, paused: false);

        assert(!counter.IsPaused, "The counter remained paused after resuming.");
        assert(
            counter.TPS == 4.0,
            "Ticks from before or during the pause leaked into the new sample.");
    }

    private static void HandlesZeroElapsedAndBackwardTime(
        Action<bool, string> assert)
    {
        TickRateCounter counter = new(100L);
        counter.Reset(2_000L);
        counter.RecordTick();
        counter.Update(2_000L, paused: false);

        assert(
            counter.TPS == 0.0 &&
            counter.WindowTickCount == 1L &&
            counter.WindowElapsedStopwatchTicks == 0L,
            "A zero-elapsed update was not handled safely.");

        counter.Update(1_999L, paused: false);
        assert(
            counter.TPS == 0.0 && counter.WindowTickCount == 0L,
            "A backward timestamp retained stale sample data.");
    }

    private static void ResetAcceptsAnAbsoluteTimestamp(
        Action<bool, string> assert)
    {
        TickRateCounter counter = new(10L);
        counter.Reset(9_000_000L);
        counter.RecordTick();
        counter.Update(9_000_010L, paused: false);

        assert(
            counter.TPS == 1.0,
            "A large absolute timestamp changed the measured rate.");

        counter.Reset(4_000_000_000L);
        counter.Update(4_000_000_000L, paused: false);
        assert(
            counter.TPS == 0.0 && counter.WindowElapsedStopwatchTicks == 0L,
            "Reset did not establish a fresh absolute-time window.");
    }
}
