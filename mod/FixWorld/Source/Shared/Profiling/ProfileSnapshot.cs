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
        private readonly Dictionary<TKey, int> indices;
        private readonly ProfileMeasurement<TKey>[] measurements;

        internal ProfileSnapshot(
            ProfileMeasurement<TKey>[] measurements,
            IEqualityComparer<TKey> keyComparer)
        {
            this.measurements = measurements ??
                throw new ArgumentNullException(nameof(measurements));
            indices = new Dictionary<TKey, int>(
                measurements.Length,
                keyComparer);
            for (int index = 0; index < measurements.Length; index++)
            {
                indices.Add(measurements[index].Key, index);
            }
        }

        public int Count => measurements.Length;

        public bool TryGet(
            TKey key,
            out ProfileMeasurement<TKey> measurement)
        {
            if (indices.TryGetValue(key, out int index))
            {
                measurement = measurements[index];
                return true;
            }

            measurement = null;
            return false;
        }

        public IEnumerator<ProfileMeasurement<TKey>> GetEnumerator()
        {
            return ((IEnumerable<ProfileMeasurement<TKey>>)measurements)
                .GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
