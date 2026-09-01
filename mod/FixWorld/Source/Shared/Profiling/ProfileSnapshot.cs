using System;
using System.Collections;
using System.Collections.Generic;

namespace FixWorld.Profiling
{
    public sealed class ProfileMeasurement<TKey>
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
            TotalTime = TimeSpan.FromTicks(totalTicks);
            MinimumTime = TimeSpan.FromTicks(minimumTicks);
            MaximumTime = TimeSpan.FromTicks(maximumTicks);
            AverageTime = calls == 0L
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(totalTicks / calls);
        }

        public TKey Key { get; }

        public long Calls { get; }

        public long Failures { get; }

        public TimeSpan TotalTime { get; }

        public TimeSpan MinimumTime { get; }

        public TimeSpan MaximumTime { get; }

        public TimeSpan AverageTime { get; }
    }

    public sealed class ProfileSnapshot<TKey> :
        IEnumerable<ProfileMeasurement<TKey>>
    {
        private readonly Dictionary<TKey, ProfileMeasurement<TKey>> measurements;

        internal ProfileSnapshot(
            IEnumerable<ProfileMeasurement<TKey>> measurements,
            IEqualityComparer<TKey> keyComparer)
        {
            this.measurements = new Dictionary<TKey, ProfileMeasurement<TKey>>(
                keyComparer);
            foreach (ProfileMeasurement<TKey> measurement in measurements)
            {
                this.measurements.Add(measurement.Key, measurement);
            }
        }

        public int Count => measurements.Count;

        public bool TryGet(
            TKey key,
            out ProfileMeasurement<TKey> measurement)
        {
            return measurements.TryGetValue(key, out measurement);
        }

        public IEnumerator<ProfileMeasurement<TKey>> GetEnumerator()
        {
            return measurements.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
