using System;
using System.Collections.Generic;
using System.Threading;

namespace FixWorld.Profiling
{
    public sealed class Profiler<TKey>
    {
        private readonly IEqualityComparer<TKey> keyComparer;
        private readonly Dictionary<TKey, ProfileSlot<TKey>> slots;
        private readonly object sync = new object();
        private ProfileSlot<TKey>[] slotSnapshot =
            Array.Empty<ProfileSlot<TKey>>();

        public Profiler(IEqualityComparer<TKey> keyComparer = null)
        {
            this.keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            slots = new Dictionary<TKey, ProfileSlot<TKey>>(this.keyComparer);
        }

        public ProfileSlot<TKey> GetSlot(TKey key)
        {
            ValidateKey(key);
            lock (sync)
            {
                if (slots.TryGetValue(key, out ProfileSlot<TKey> existing))
                {
                    return existing;
                }

                ProfileSlot<TKey> created = new ProfileSlot<TKey>(key);
                slots.Add(key, created);
                ProfileSlot<TKey>[] current = slotSnapshot;
                ProfileSlot<TKey>[] updated =
                    new ProfileSlot<TKey>[current.Length + 1];
                Array.Copy(current, updated, current.Length);
                updated[current.Length] = created;
                Volatile.Write(ref slotSnapshot, updated);
                return created;
            }
        }

        public ProfileScope<TKey> Measure(TKey key)
        {
            return GetSlot(key).Measure();
        }

        public void Observe(
            TKey key,
            TimeSpan elapsed,
            bool succeeded = true)
        {
            GetSlot(key).Observe(elapsed, succeeded);
        }

        public ProfileSnapshot<TKey> Snapshot()
        {
            ProfileSlot<TKey>[] current = Volatile.Read(ref slotSnapshot);
            ProfileMeasurement<TKey>[] measurements =
                new ProfileMeasurement<TKey>[current.Length];
            for (int index = 0; index < current.Length; index++)
            {
                measurements[index] = current[index].Snapshot();
            }

            return new ProfileSnapshot<TKey>(measurements, keyComparer);
        }

        private static void ValidateKey(TKey key)
        {
            if ((object)key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
        }

    }

    public sealed class ProfileSlot<TKey>
    {
        private readonly object sync = new object();
        private long calls;
        private long failures;
        private long maximumTicks;
        private long minimumTicks;
        private long totalTicks;

        internal ProfileSlot(TKey key)
        {
            Key = key;
        }

        public TKey Key { get; }

        public ProfileScope<TKey> Measure()
        {
            return new ProfileScope<TKey>(this);
        }

        public void Observe(TimeSpan elapsed, bool succeeded = true)
        {
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsed),
                    elapsed,
                    "Profile duration cannot be negative.");
            }

            long elapsedTicks = elapsed.Ticks;
            lock (sync)
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
        }

        public ProfileMeasurement<TKey> Snapshot()
        {
            lock (sync)
            {
                return new ProfileMeasurement<TKey>(
                    Key,
                    calls,
                    failures,
                    totalTicks,
                    minimumTicks,
                    maximumTicks);
            }
        }
    }
}
