// SPDX-License-Identifier: MPL-2.0
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace FixWorld.Profiling
{
    internal sealed class ProfilerState<TKey> : IDisposable
    {
        private readonly ProfileAggregationMode aggregationMode;
        private readonly ProfilePump<TKey> pump;
        private int disposed;
        private int enabled;

        internal ProfilerState(
            ProfilerOptions options,
            Func<ProfileSnapshot<TKey>> publishSnapshot)
        {
            aggregationMode = options.AggregationMode;
            enabled = options.Enabled ? 1 : 0;
            if (aggregationMode == ProfileAggregationMode.Buffered)
            {
                pump = new ProfilePump<TKey>(
                    options.PublishInterval,
                    publishSnapshot);
            }
        }

        internal bool Enabled =>
            Volatile.Read(ref enabled) != 0 &&
            Volatile.Read(ref disposed) == 0;

        internal void SetEnabled(bool value) =>
            Volatile.Write(ref enabled, value ? 1 : 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long StartTimestamp() =>
            Enabled
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : ProfileSlot<TKey>.InactiveTimestamp;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void StopTimestamp(
            ProfileSlot<TKey> slot,
            long startedAt,
            bool succeeded)
        {
            if (startedAt == ProfileSlot<TKey>.InactiveTimestamp)
            {
                return;
            }

            Observe(
                slot,
                Math.Max(
                    0L,
                    System.Diagnostics.Stopwatch.GetTimestamp() - startedAt),
                succeeded);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Observe(
            ProfileSlot<TKey> slot,
            long elapsedTicks,
            bool succeeded)
        {
            if (!Enabled)
            {
                return;
            }

            if (aggregationMode == ProfileAggregationMode.Inline)
            {
                slot.AggregateInline(elapsedTicks, succeeded);
                return;
            }

            pump.Record(slot, elapsedTicks, succeeded);
        }

        internal bool Flush()
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                if (pump == null)
                {
                    return false;
                }

                pump.Flush();
                return true;
            }

            return pump != null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref enabled, 0);
            pump?.Dispose();
        }
    }

    internal sealed class ProfilePump<TKey> : IDisposable
    {
        [ThreadStatic]
        private static int primaryOwnerId;

        [ThreadStatic]
        private static ProfileShard<TKey> primaryShard;

        [ThreadStatic]
        private static int secondaryOwnerId;

        [ThreadStatic]
        private static ProfileShard<TKey> secondaryShard;

        private static int nextOwnerId;

        private readonly object flushSync = new();
        private readonly object shardSync = new();
        private readonly ManualResetEventSlim flushCompleted = new(false);
        private readonly Func<ProfileSnapshot<TKey>> publishSnapshot;
        private readonly int publishIntervalMilliseconds;
        private readonly Thread thread;
        private readonly AutoResetEvent wake = new(false);
        private readonly int ownerId;
        private ProfileShard<TKey>[] shards = [];
        private int disposed;
        private int flushRequested;
        private int stopping;

        internal ProfilePump(
            TimeSpan publishInterval,
            Func<ProfileSnapshot<TKey>> publishSnapshot)
        {
            this.publishSnapshot = publishSnapshot ??
                throw new ArgumentNullException(nameof(publishSnapshot));
            publishIntervalMilliseconds = Math.Max(
                1,
                (int)Math.Min(int.MaxValue, publishInterval.TotalMilliseconds));
            ownerId = Interlocked.Increment(ref nextOwnerId);
            thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "FixWorld profiler"
            };
            thread.Start();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Record(
            ProfileSlot<TKey> slot,
            long elapsedTicks,
            bool succeeded)
        {
            if (Volatile.Read(ref stopping) != 0)
            {
                return;
            }

            ProfileShard<TKey> shard = primaryShard;
            if (primaryOwnerId != ownerId || shard == null)
            {
                if (secondaryOwnerId == ownerId && secondaryShard != null)
                {
                    int previousOwnerId = primaryOwnerId;
                    ProfileShard<TKey> previousShard = primaryShard;
                    primaryOwnerId = secondaryOwnerId;
                    primaryShard = secondaryShard;
                    secondaryOwnerId = previousOwnerId;
                    secondaryShard = previousShard;
                    shard = primaryShard;
                }
                else
                {
                    secondaryOwnerId = primaryOwnerId;
                    secondaryShard = primaryShard;
                    shard = RegisterShard();
                    primaryOwnerId = ownerId;
                    primaryShard = shard;
                }
            }

            shard.Record(slot, elapsedTicks, succeeded);
        }

        internal void Flush()
        {
            if (Volatile.Read(ref stopping) != 0)
            {
                return;
            }

            lock (flushSync)
            {
                flushCompleted.Reset();
                Volatile.Write(ref flushRequested, 1);
                wake.Set();
                flushCompleted.Wait();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref stopping, 1);
            wake.Set();
            thread.Join();
            flushCompleted.Dispose();
            wake.Dispose();
        }

        private ProfileShard<TKey> RegisterShard()
        {
            ProfileShard<TKey> created = new();
            lock (shardSync)
            {
                ProfileShard<TKey>[] current = shards;
                ProfileShard<TKey>[] updated =
                    new ProfileShard<TKey>[current.Length + 1];
                Array.Copy(current, updated, current.Length);
                updated[current.Length] = created;
                Volatile.Write(ref shards, updated);
            }

            return created;
        }

        private void Run()
        {
            while (Volatile.Read(ref stopping) == 0)
            {
                wake.WaitOne(publishIntervalMilliseconds);
                if (Volatile.Read(ref stopping) != 0)
                {
                    break;
                }

                bool flush = Volatile.Read(ref flushRequested) != 0;
                if (DrainShards() || flush)
                {
                    publishSnapshot();
                }

                CompleteFlush();
            }

            DrainShards();
            publishSnapshot();
            CompleteFlush();
        }

        private bool DrainShards()
        {
            bool changed = false;
            ProfileShard<TKey>[] current = Volatile.Read(ref shards);
            foreach (ProfileShard<TKey> shard in current)
            {
                changed |= shard.Drain();
            }

            return changed;
        }

        private void CompleteFlush()
        {
            if (Interlocked.Exchange(ref flushRequested, 0) != 0)
            {
                flushCompleted.Set();
            }
        }
    }

    internal sealed class ProfileShard<TKey>
    {
        private ProfileAccumulator<TKey>[] first = [];
        private ProfileAccumulator<TKey>[] second = [];
        private int activeBuffer;
        private int writing;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Record(
            ProfileSlot<TKey> slot,
            long elapsedTicks,
            bool succeeded)
        {
            Volatile.Write(ref writing, 1);
            EnsureCapacity(slot.Index + 1);
            ProfileAccumulator<TKey>[] target =
                Volatile.Read(ref activeBuffer) == 0 ? first : second;
            target[slot.Index].Record(slot, elapsedTicks, succeeded);
            Volatile.Write(ref writing, 0);
        }

        internal bool Drain()
        {
            bool changed = false;
            int sourceIndex = Interlocked.Exchange(
                ref activeBuffer,
                Volatile.Read(ref activeBuffer) == 0 ? 1 : 0);
            SpinWait spinner = new();
            while (Volatile.Read(ref writing) != 0)
            {
                spinner.SpinOnce();
            }

            ProfileAccumulator<TKey>[] source =
                sourceIndex == 0 ? first : second;
            for (int index = 0; index < source.Length; index++)
            {
                ref ProfileAccumulator<TKey> accumulator = ref source[index];
                if (accumulator.Calls == 0L)
                {
                    continue;
                }

                accumulator.Slot.AggregateBuffered(
                    accumulator.Calls,
                    accumulator.Failures,
                    accumulator.TotalTicks,
                    accumulator.MinimumTicks,
                    accumulator.MaximumTicks);
                accumulator = default;
                changed = true;
            }

            return changed;
        }

        private void EnsureCapacity(int required)
        {
            if (first.Length >= required)
            {
                return;
            }

            int capacity = Math.Max(required, Math.Max(4, first.Length * 2));
            Array.Resize(ref first, capacity);
            Array.Resize(ref second, capacity);
        }
    }

    internal struct ProfileAccumulator<TKey>
    {
        internal ProfileSlot<TKey> Slot;
        internal long Calls;
        internal long Failures;
        internal long MaximumTicks;
        internal long MinimumTicks;
        internal long TotalTicks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Record(
            ProfileSlot<TKey> slot,
            long elapsedTicks,
            bool succeeded)
        {
            if (Calls == 0L)
            {
                Slot = slot;
                MinimumTicks = elapsedTicks;
                MaximumTicks = elapsedTicks;
            }
            else
            {
                if (elapsedTicks < MinimumTicks)
                {
                    MinimumTicks = elapsedTicks;
                }

                if (elapsedTicks > MaximumTicks)
                {
                    MaximumTicks = elapsedTicks;
                }
            }

            Calls++;
            TotalTicks += elapsedTicks;
            if (!succeeded)
            {
                Failures++;
            }
        }
    }
}
