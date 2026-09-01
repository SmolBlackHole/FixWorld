using System;
using System.Collections.Generic;

namespace FixWorld.Profiling
{
    public sealed class Profiler<TKey>
    {
        private readonly IEqualityComparer<TKey> keyComparer;
        private readonly Dictionary<TKey, Aggregate> measurements;
        private readonly object sync = new object();

        public Profiler(IEqualityComparer<TKey> keyComparer = null)
        {
            this.keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            measurements = new Dictionary<TKey, Aggregate>(this.keyComparer);
        }

        public ProfileScope<TKey> Measure(TKey key)
        {
            ValidateKey(key);
            return new ProfileScope<TKey>(this, key);
        }

        public void Observe(
            TKey key,
            TimeSpan elapsed,
            bool succeeded = true)
        {
            ValidateKey(key);
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsed),
                    elapsed,
                    "Profile duration cannot be negative.");
            }

            lock (sync)
            {
                if (!measurements.TryGetValue(key, out Aggregate aggregate))
                {
                    aggregate = new Aggregate();
                    measurements.Add(key, aggregate);
                }

                aggregate.Observe(elapsed.Ticks, succeeded);
            }
        }

        public ProfileSnapshot<TKey> Snapshot()
        {
            lock (sync)
            {
                List<ProfileMeasurement<TKey>> snapshot =
                    new List<ProfileMeasurement<TKey>>(measurements.Count);
                foreach (KeyValuePair<TKey, Aggregate> item in measurements)
                {
                    snapshot.Add(item.Value.ToMeasurement(item.Key));
                }

                return new ProfileSnapshot<TKey>(snapshot, keyComparer);
            }
        }

        private static void ValidateKey(TKey key)
        {
            if ((object)key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
        }

        private sealed class Aggregate
        {
            private long calls;
            private long failures;
            private long maximumTicks;
            private long minimumTicks;
            private long totalTicks;

            internal void Observe(long elapsedTicks, bool succeeded)
            {
                if (calls == 0L || elapsedTicks < minimumTicks)
                {
                    minimumTicks = elapsedTicks;
                }

                if (elapsedTicks > maximumTicks)
                {
                    maximumTicks = elapsedTicks;
                }

                calls++;
                totalTicks += elapsedTicks;
                if (!succeeded)
                {
                    failures++;
                }
            }

            internal ProfileMeasurement<TKey> ToMeasurement(TKey key)
            {
                return new ProfileMeasurement<TKey>(
                    key,
                    calls,
                    failures,
                    totalTicks,
                    minimumTicks,
                    maximumTicks);
            }
        }
    }
}
