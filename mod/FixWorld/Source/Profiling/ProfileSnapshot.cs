// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace FixWorld.Profiling
{
    public readonly struct ProfileMeasurement<TKey>
    {
        internal ProfileMeasurement(
            TKey key,
            long calls,
            long failures,
            long totalTicks,
            long minimumTicks,
            long maximumTicks)
        {
            Key = key;
            Calls = calls;
            Failures = failures;
            TotalStopwatchTicks = totalTicks;
            MinimumStopwatchTicks = minimumTicks;
            MaximumStopwatchTicks = maximumTicks;
        }

        public TKey Key { get; }

        public long Calls { get; }

        public long Failures { get; }

        public long TotalStopwatchTicks { get; }

        public long MinimumStopwatchTicks { get; }

        public long MaximumStopwatchTicks { get; }

        public long AverageStopwatchTicks =>
            Calls == 0L ? 0L : TotalStopwatchTicks / Calls;

        public TimeSpan TotalTime =>
            ProfileTime.ToTimeSpan(TotalStopwatchTicks);

        public TimeSpan MinimumTime =>
            ProfileTime.ToTimeSpan(MinimumStopwatchTicks);

        public TimeSpan MaximumTime =>
            ProfileTime.ToTimeSpan(MaximumStopwatchTicks);

        public TimeSpan AverageTime =>
            ProfileTime.ToTimeSpan(AverageStopwatchTicks);
    }

    public sealed class ProfileSnapshot<TKey> :
        IReadOnlyList<ProfileMeasurement<TKey>>
    {
        private readonly IEqualityComparer<TKey> keyComparer;
        private readonly ProfileMeasurement<TKey>[] measurements;

        internal ProfileSnapshot(
            ProfileMeasurement<TKey>[] measurements,
            IEqualityComparer<TKey> keyComparer)
        {
            this.measurements = measurements ??
                throw new ArgumentNullException(nameof(measurements));
            this.keyComparer = keyComparer ??
                throw new ArgumentNullException(nameof(keyComparer));
            PublishedAtTimestamp = Stopwatch.GetTimestamp();
        }

        public int Count => measurements.Length;

        public long PublishedAtTimestamp { get; }

        public TimeSpan Age =>
            ProfileTime.ToTimeSpan(
                Math.Max(0L, Stopwatch.GetTimestamp() - PublishedAtTimestamp));

        public ProfileMeasurement<TKey> this[int index] => measurements[index];

        public bool TryGet(
            TKey key,
            out ProfileMeasurement<TKey> measurement)
        {
            for (int index = 0; index < measurements.Length; index++)
            {
                ref readonly ProfileMeasurement<TKey> candidate =
                    ref measurements[index];
                if (keyComparer.Equals(candidate.Key, key))
                {
                    measurement = candidate;
                    return true;
                }
            }

            measurement = default;
            return false;
        }

        public IEnumerator<ProfileMeasurement<TKey>> GetEnumerator() =>
            ((IEnumerable<ProfileMeasurement<TKey>>)measurements)
            .GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
