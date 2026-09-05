// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace FixWorld.Profiling
{
    public sealed class Profiler<TKey> : IDisposable
    {
        private readonly IEqualityComparer<TKey> keyComparer;
        private readonly ProfilerState<TKey> state;
        private readonly Dictionary<TKey, ProfileSlot<TKey>> slots;
        private readonly object registrySync = new();
        private ProfileSlot<TKey>[] slotSnapshot = [];
        private ProfileSnapshot<TKey> publishedSnapshot;
        private int disposed;

        public Profiler(
            IEqualityComparer<TKey> keyComparer = null,
            ProfilerOptions options = null)
        {
            ProfilerOptions resolvedOptions = options ?? ProfilerOptions.Inline;
            this.keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            slots = new(this.keyComparer);
            publishedSnapshot = new([], this.keyComparer);
            AggregationMode = resolvedOptions.AggregationMode;
            state = new(
                resolvedOptions,
                PublishSnapshotCore);
        }

        public ProfileAggregationMode AggregationMode { get; }

        public bool Enabled => state.Enabled;

        public ProfileSnapshot<TKey> PublishedSnapshot =>
            Volatile.Read(ref publishedSnapshot);

        public void SetEnabled(bool enabled)
        {
            ThrowIfDisposed();
            state.SetEnabled(enabled);
        }

        public ProfileSlot<TKey> GetSlot(TKey key)
        {
            ValidateKey(key);
            ThrowIfDisposed();
            lock (registrySync)
            {
                if (slots.TryGetValue(key, out ProfileSlot<TKey> existing))
                {
                    return existing;
                }

                ProfileSlot<TKey> created = new(
                    key,
                    currentIndex: slotSnapshot.Length,
                    state);
                slots.Add(key, created);
                ProfileSlot<TKey>[] current = slotSnapshot;
                var updated =
                    new ProfileSlot<TKey>[current.Length + 1];
                Array.Copy(current, updated, current.Length);
                updated[current.Length] = created;
                Volatile.Write(ref slotSnapshot, updated);
                return created;
            }
        }

        public ProfileScope<TKey> Measure(TKey key) => GetSlot(key).Measure();

        public void Observe(
            TKey key,
            TimeSpan elapsed,
            bool succeeded = true) =>
            GetSlot(key).Observe(elapsed, succeeded);

        public void ObserveStopwatchTicks(
            TKey key,
            long elapsedTicks,
            bool succeeded = true) =>
            GetSlot(key).ObserveStopwatchTicks(elapsedTicks, succeeded);

        public ProfileSnapshot<TKey> Snapshot() => PublishSnapshot();

        public ProfileSnapshot<TKey> PublishSnapshot()
        {
            return state.Flush()
                ? PublishedSnapshot
                : PublishSnapshotCore();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            state.Dispose();
        }

        private ProfileSnapshot<TKey> PublishSnapshotCore()
        {
            ProfileSlot<TKey>[] current = Volatile.Read(ref slotSnapshot);
            var measurements =
                new ProfileMeasurement<TKey>[current.Length];
            for (int index = 0; index < current.Length; index++)
            {
                measurements[index] = current[index].CaptureSnapshot();
            }

            ProfileSnapshot<TKey> snapshot = new(measurements, keyComparer);
            Volatile.Write(ref publishedSnapshot, snapshot);
            return snapshot;
        }

        private static void ValidateKey(TKey key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(Profiler<TKey>));
            }
        }
    }

    public sealed class ProfileSlot<TKey>
    {
        internal const long InactiveTimestamp = long.MinValue;

        private readonly ProfilerState<TKey> state;
        private long calls;
        private long failures;
        private long maximumTicks;
        private long minimumTicks = long.MaxValue;
        private long totalTicks;

        internal ProfileSlot(
            TKey key,
            int currentIndex,
            ProfilerState<TKey> state)
        {
            Key = key;
            Index = currentIndex;
            this.state = state;
        }

        public TKey Key { get; }

        internal int Index { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ProfileScope<TKey> Measure() => new(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long StartTimestamp() => state.StartTimestamp();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StopTimestamp(long startedAt, bool succeeded = true) =>
            state.StopTimestamp(this, startedAt, succeeded);

        public void Observe(TimeSpan elapsed, bool succeeded = true)
        {
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsed),
                    elapsed,
                    "Profile duration cannot be negative.");
            }

            ObserveStopwatchTicks(
                ProfileTime.ToStopwatchTicks(elapsed),
                succeeded);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ObserveStopwatchTicks(
            long elapsedTicks,
            bool succeeded = true)
        {
            if (elapsedTicks < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedTicks),
                    elapsedTicks,
                    "Profile duration cannot be negative.");
            }

            state.Observe(this, elapsedTicks, succeeded);
        }

        public ProfileMeasurement<TKey> Snapshot()
        {
            state.Flush();
            return CaptureSnapshot();
        }

        internal void AggregateBuffered(
            long capturedCalls,
            long capturedFailures,
            long capturedTotalTicks,
            long capturedMinimumTicks,
            long capturedMaximumTicks)
        {
            calls += capturedCalls;
            failures += capturedFailures;
            totalTicks += capturedTotalTicks;
            if (capturedMinimumTicks < minimumTicks)
            {
                minimumTicks = capturedMinimumTicks;
            }

            if (capturedMaximumTicks > maximumTicks)
            {
                maximumTicks = capturedMaximumTicks;
            }
        }

        internal void AggregateInline(long elapsedTicks, bool succeeded)
        {
            Interlocked.Increment(ref calls);
            Interlocked.Add(ref totalTicks, elapsedTicks);
            UpdateMinimum(elapsedTicks);
            UpdateMaximum(elapsedTicks);
            if (!succeeded)
            {
                Interlocked.Increment(ref failures);
            }
        }

        internal ProfileMeasurement<TKey> CaptureSnapshot()
        {
            long capturedCalls = Interlocked.Read(ref calls);
            long capturedMinimum = Interlocked.Read(ref minimumTicks);
            return new ProfileMeasurement<TKey>(
                Key,
                capturedCalls,
                Interlocked.Read(ref failures),
                Interlocked.Read(ref totalTicks),
                capturedCalls == 0L || capturedMinimum == long.MaxValue
                    ? 0L
                    : capturedMinimum,
                Interlocked.Read(ref maximumTicks));
        }

        private void UpdateMinimum(long elapsedTicks)
        {
            long current = Interlocked.Read(ref minimumTicks);
            while (elapsedTicks < current)
            {
                long observed = Interlocked.CompareExchange(
                    ref minimumTicks,
                    elapsedTicks,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }

        private void UpdateMaximum(long elapsedTicks)
        {
            long current = Interlocked.Read(ref maximumTicks);
            while (elapsedTicks > current)
            {
                long observed = Interlocked.CompareExchange(
                    ref maximumTicks,
                    elapsedTicks,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    internal static class ProfileTime
    {
        internal static long ToStopwatchTicks(TimeSpan elapsed) =>
            (long)Math.Round(
                elapsed.Ticks *
                ((double)Stopwatch.Frequency / TimeSpan.TicksPerSecond));

        internal static TimeSpan ToTimeSpan(long stopwatchTicks) =>
            stopwatchTicks == 0L
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(
                    (long)Math.Round(
                        stopwatchTicks *
                        ((double)TimeSpan.TicksPerSecond /
                         Stopwatch.Frequency)));
    }
}
