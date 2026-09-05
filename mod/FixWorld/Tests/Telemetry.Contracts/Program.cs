// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FixWorld.Profiling;
using FixWorld.Telemetry;
using FixWorld.Utils;

internal static class Program
{
    private static int checks;
    private sealed class Data { public Data(long value, string text = null) { Value = value; Text = text; } public long Value { get; } public string Text { get; } }
    private static TelemetryContract<Data> Contract(string id = "test") => new(id, 2, (data, writer) =>
    { writer.Value("value", data.Value); writer.Value("text", data.Text); writer.Value("ratio", 1.25); writer.Value("ok", true); });
    private static void Main()
    {
        Registry(); Profiles(); Publication(); Scheduler(); CaptureContracts.Run();
        Console.WriteLine($"PASS: {checks} telemetry/profiling contract checks (.NET Framework). No game started.");
    }
    private static void Registry()
    {
        using var store = new TelemetryStore();
        Expect<ArgumentException>(() => Contract(" "));
        Expect<ArgumentOutOfRangeException>(() => new TelemetryContract<Data>("x", 0, (d, w) => { }));
        var registration = store.Register(Contract());
        var membership = store.Registrations;
        Require(ReferenceEquals(membership, store.Registrations), "Membership reads do not copy");
        Expect<InvalidOperationException>(() => store.Register(Contract()));
        Require(registration.SnapshotType == typeof(Data) && registration.SchemaVersion == 2, "Typed metadata");
        Expect<ArgumentNullException>(() => registration.Publish(null));
        var retained = new Data(1, "quote\"slash\\line\n\t\u0001\ud83d\ude00");
        registration.Publish(retained);
        Require(ReferenceEquals(retained, registration.Snapshot), "Zero-copy DTO publication/read");
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            using var json = new StringWriter(); using var log = new StringWriter();
            store.WriteJson(json); store.WriteLog(log);
            var parsed = new JavaScriptSerializer().Deserialize<object[]>(json.ToString());
            var record = (Dictionary<string, object>)parsed[0];
            var values = (Dictionary<string, object>)record["values"];
            Require((string)values["text"] == retained.Text && Convert.ToDouble(values["ratio"]) == 1.25, "JSON round-trip/culture/escaping");
            Require(log.ToString().Contains("ratio: 1.25") && log.ToString().Contains("value: 1"), "One presentation contract, log and JSON");
        }
        finally { Thread.CurrentThread.CurrentCulture = previousCulture; }
        registration.Publish(new Data(2));
        Require(retained.Value == 1 && registration.Snapshot.Value == 2, "Retained immutable view");
        registration.Dispose(); registration.Dispose();
        var replacement = store.Register(Contract());
        registration.Dispose();
        Require(store.Registrations.Count == 1 && membership.Count == 1, "Old handle cannot remove replacement; old list remains valid");
        Expect<ObjectDisposedException>(() => registration.Publish(new Data(3)));
        Parallel.For(0, 500, i => replacement.Publish(new Data(i)));
        Require(replacement.Snapshot != null, "Concurrent publication");
        store.Dispose(); store.Dispose();
        Expect<ObjectDisposedException>(() => store.Register(Contract("new")));
        Expect<ObjectDisposedException>(() => replacement.Publish(new Data(4)));
        Require(store.Registrations.Count == 0 && replacement.Snapshot != null, "Disposal retires writers, retains last view");
    }
    private static void Profiles()
    {
        var own = new ProfileKey("test", "operation");
        var original = new ProfileKey("test", "operation", ProfileSource.RimWorld);
        Require(!own.Equals(original), "Original and replacement separated");
        foreach (var options in new[] { ProfilerOptions.Inline, ProfilerOptions.Buffered })
        {
            using var profiler = new Profiler<ProfileKey>(options: options);
            var slot = profiler.GetSlot(own);
            Require(ReferenceEquals(slot, profiler.GetSlot(own)), "Slot resolved once");
            Parallel.For(0, 4000, _ => slot.ObserveStopwatchTicks(10));
            var scope = slot.Measure(); scope.Fail(); scope.Dispose();
            var result = profiler.PublishSnapshot();
            Require(result.TryGet(own, out var measurement) && measurement.Calls == 4001 && measurement.Failures == 1, "Concurrent exact aggregation and fail-once scope");
            profiler.GetSlot(original).ObserveStopwatchTicks(20);
            profiler.SetEnabled(false);
            slot.ObserveStopwatchTicks(30);
            using (slot.Measure()) { }
            Require(profiler.PublishSnapshot().TryGet(own, out var disabled) && disabled.Calls == 4001, "Disabled profiler records nothing");
            Require(result.Count == 1, "Profile snapshot remains stable after registry changes");
        }
        using var bench = new Profiler<int>(); var probe = bench.GetSlot(0);
        using (var failed = probe.Measure()) { failed.Fail(); }
        Require(bench.PublishSnapshot().TryGet(0, out var failedUsing) && failedUsing.Calls == 1 && failedUsing.Failures == 1,
            "Using-scope failure is not counted again on disposal");
        for (int i = 0; i < 10000; i++) { using (probe.Measure()) { } }
        int gc = GC.CollectionCount(0); var timer = Stopwatch.StartNew();
        for (int i = 0; i < 100000; i++) { using (probe.Measure()) { } }
        timer.Stop();
        Console.WriteLine($"Probe smoke: {timer.Elapsed.TotalMilliseconds:F2} ms / 100000 scopes; Gen0 delta {GC.CollectionCount(0) - gc}. Desktop CLR, not Unity Mono.");
    }
    private static void Publication()
    {
        using var diagnostics = new LibraryDiagnostics(100);
        int captures = 0;
        Func<LibraryState> capture = () => { captures++; return new LibraryState(2, 3, new TickSchedulerSnapshot(4, 5, 6)); };
        diagnostics.Profiler.SetEnabled(false);
        diagnostics.RecordFrame(); diagnostics.RecordTick(); diagnostics.RecordCallbackError();
        Require(diagnostics.PublishIfDue(1000, capture), "First publication");
        var first = diagnostics.Snapshot;
        Require(!diagnostics.PublishIfDue(1099, capture) && captures == 1, "No capture/allocation before deadline");
        diagnostics.RecordTick();
        Require(diagnostics.PublishIfDue(1100, capture) && diagnostics.Snapshot.Ticks == 2 && first.Ticks == 1, "Cadence and independent business counters");
        Require(diagnostics.Snapshot.Errors == 1 && diagnostics.Snapshot.State.Distributed.Recipients == 4, "Typed library state");
        using var json = new StringWriter(); diagnostics.Store.WriteJson(json);
        Require(new JavaScriptSerializer().Deserialize<object[]>(json.ToString()).Length == 1, "Actual library presentation produces valid JSON");
        Require(diagnostics.PublishIfDue(1, capture), "Clock regression starts a new publication window");
        diagnostics.Dispose();
        Require(!diagnostics.PublishIfDue(10000, capture), "Retired facade does not read providers");
    }
    private static void Scheduler()
    {
        var scheduler = new DistributedTickScheduler(); scheduler.Initialize(0);
        var owner = new Verse.Thing(); int calls = 0;
        scheduler.RegisterTickability(() => calls++, 1, owner); scheduler.Tick(1);
        var state = scheduler.CaptureTelemetry();
        Require(state.Recipients == 1 && state.Intervals == 1 && state.LastTickCallbacks == 1 && calls == 1, "Production scheduler snapshot");
        owner.Spawned = false; scheduler.Tick(2);
        Require(scheduler.CaptureTelemetry().Recipients == 0 && state.Recipients == 1, "Despawn behavior unchanged; captured values stable");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static void Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
}
