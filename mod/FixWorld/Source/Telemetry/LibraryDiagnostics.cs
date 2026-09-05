// SPDX-License-Identifier: MPL-2.0
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using FixWorld.Profiling;

namespace FixWorld.Telemetry
{
    public readonly struct TickSchedulerSnapshot
    {
        public TickSchedulerSnapshot(int recipients, int intervals, int lastTickCallbacks)
        { Recipients = recipients; Intervals = intervals; LastTickCallbacks = lastTickCallbacks; }
        public int Recipients { get; }
        public int Intervals { get; }
        public int LastTickCallbacks { get; }
    }

    public readonly struct LibraryState(int initializedMods, int delayedCallbacks, TickSchedulerSnapshot distributed)
    {
        public int InitializedMods { get; } = initializedMods; public int DelayedCallbacks { get; } = delayedCallbacks; public TickSchedulerSnapshot Distributed { get; } = distributed;

    }

    public sealed class LibrarySnapshot
    {
        internal LibrarySnapshot(long timestamp, long frames, long ticks, long errors,
            LibraryState state, ProfileSnapshot<ProfileKey> profile)
        { Timestamp = timestamp; Frames = frames; Ticks = ticks; Errors = errors; State = state; Profile = profile; }
        public long Timestamp { get; }
        public long Frames { get; }
        public long Ticks { get; }
        public long Errors { get; }
        public LibraryState State { get; }
        public ProfileSnapshot<ProfileKey> Profile { get; }

        public static TelemetryContract<LibrarySnapshot> Contract { get; } = new("fixworld.library", 1, Present);
        private static void Present(LibrarySnapshot data, TelemetryWriter writer)
        {
            writer.Value("published_stopwatch_ticks", data.Timestamp);
            writer.Value("stopwatch_frequency", Stopwatch.Frequency);
            writer.Counter("frame_notifications", data.Frames);
            writer.Counter("tick_notifications", data.Ticks);
            writer.Counter("caught_callback_errors", data.Errors);
            writer.Value("initialized_mods", data.State.InitializedMods);
            writer.Value("delayed_callbacks", data.State.DelayedCallbacks);
            writer.Value("distributed.recipients", data.State.Distributed.Recipients);
            writer.Value("distributed.intervals", data.State.Distributed.Intervals);
            writer.Value("distributed.last_tick_callbacks", data.State.Distributed.LastTickCallbacks);
            for (int index = 0; index < data.Profile.Count; index++)
            {
                var measurement = data.Profile[index];
                // Index-qualified fields avoid ambiguity if an owner/operation contains separators.
                var prefix = "profile." + index.ToString(CultureInfo.InvariantCulture) + ".";
                writer.Value(prefix + "owner", measurement.Key.Owner);
                writer.Value(prefix + "operation", measurement.Key.Operation);
                writer.Value(prefix + "source", measurement.Key.Source.ToString());
                writer.Counter(prefix + "calls", measurement.Calls);
                writer.Counter(prefix + "failures", measurement.Failures);
                writer.Counter(prefix + "inclusive_total_ms", measurement.TotalTime.TotalMilliseconds);
                writer.Value(prefix + "max_ms", measurement.MaximumTime.TotalMilliseconds);
            }
        }
    }

    // Controller owns this facade. It owns no gameplay state, worker scheduler,
    // events or lifecycle framework. The profiler and registry remain independent.
    public sealed class LibraryDiagnostics : IDisposable
    {
        private readonly TelemetryRegistration<LibrarySnapshot> library;
        private readonly long interval;
        private long lastPublication;
        private bool published;
        private long frames, ticks, errors;
        private int disposed;

        public LibraryDiagnostics(long? publicationIntervalTicks = null)
        {
            interval = publicationIntervalTicks ?? Math.Max(1L, Stopwatch.Frequency / 2);
            if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(publicationIntervalTicks));
            Store = new TelemetryStore();
            Profiler = new Profiler<ProfileKey>(); // Inline: no additional aggregation thread.
            library = Store.Register(LibrarySnapshot.Contract);
            Update = Slot("Update"); Tick = Slot("Tick"); FixedUpdate = Slot("FixedUpdate"); OnGUI = Slot("OnGUI");
        }
        public TelemetryStore Store { get; }
        public Profiler<ProfileKey> Profiler { get; }
        public ProfileSlot<ProfileKey> Update { get; }
        public ProfileSlot<ProfileKey> Tick { get; }
        public ProfileSlot<ProfileKey> FixedUpdate { get; }
        public ProfileSlot<ProfileKey> OnGUI { get; }
        public LibrarySnapshot Snapshot => library.Snapshot;
        private ProfileSlot<ProfileKey> Slot(string operation) => Profiler.GetSlot(new ProfileKey("library", operation));
        public void RecordFrame() => Interlocked.Increment(ref frames);
        public void RecordTick() => Interlocked.Increment(ref ticks);
        public void RecordCallbackError() => Interlocked.Increment(ref errors);

        // Called only by the controller's main-thread publication boundary.
        // A cached delegate ensures state is read only when a publication is due.
        public bool PublishIfDue(long timestamp, Func<LibraryState> capture)
        {
            if (Volatile.Read(ref disposed) != 0) return false;
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (published && timestamp >= lastPublication && timestamp - lastPublication < interval) return false;
            var state = capture();
            var snapshot = new LibrarySnapshot(timestamp, Interlocked.Read(ref frames),
                Interlocked.Read(ref ticks), Interlocked.Read(ref errors), state, Profiler.PublishSnapshot());
            library.Publish(snapshot);
            lastPublication = timestamp;
            published = true;
            return true;
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { Profiler.Dispose(); }
            finally { Store.Dispose(); }
        }
    }
}
