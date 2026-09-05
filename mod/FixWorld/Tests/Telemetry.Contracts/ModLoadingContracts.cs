using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FixWorld.Telemetry;

internal static class ModLoadingContracts
{
    private static int checks;
    private static void Require(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
    internal static void Run()
    {
        using var diagnostics = new LibraryDiagnostics();
        long now = 0;
        using var telemetry = new ModLoadingTelemetry(diagnostics, () => now, 1000);
        var outer = telemetry.Begin("alpha", "Alpha", ModLoadPart.Xml);
        now = 10;
        using (telemetry.Begin("beta", "Beta", null))
            telemetry.RecordMessage("child", true);
        telemetry.RecordMessage("parent", false);
        Task.Run(() => telemetry.RecordMessage("unowned worker", true)).GetAwaiter().GetResult();
        now = 30;
        outer.Dispose();
        outer.Dispose();
        using (var scope = telemetry.Begin("gamma", "Gamma", ModLoadPart.Constructors))
        { now = 50; scope.Fail(new InvalidOperationException("fixture")); }
        telemetry.Publish();
        var first = telemetry.Snapshot;
        var alpha = first.Mods.Single(m => m.Id == "alpha");
        Require(alpha.TotalMilliseconds == 30, "Timed scope and idempotent completion");
        Require(alpha.Warnings == 1 && alpha.Errors == 0, "Nested context restored");
        Require(first.Mods.Single(m => m.Id == "beta").Errors == 1, "Child context attributed");
        Require(first.Mods.Single(m => m.Id == "unattributed").Errors == 1, "Thread context does not leak");
        Require(first.Mods[0].Id == "alpha", "Sorted by observed duration");
        var profile = diagnostics.Profiler.PublishSnapshot();
        Require(profile.Single(p => p.Key.Owner == "gamma").Failures == 1, "Profiler marks escaping exceptions");
        telemetry.Publish();
        Require(ReferenceEquals(first, telemetry.Snapshot), "Idle publication does not copy");
        using (telemetry.Begin("alpha", "Alpha", null))
            for (int i = 0; i < 12; i++)
                telemetry.RecordMessage(new string('x', 3000) + i, true);
        telemetry.Publish();
        var updated = telemetry.Snapshot.Mods.Single(m => m.Id == "alpha");
        Require(updated.Errors == 12, "All message occurrences counted");
        Require(updated.Messages.Count <= 5 && updated.Messages.All(m => m.Length <= 2051), "Message retention bounded");
        Require(alpha.Errors == 0, "Published snapshots remain immutable");
        using var output = new StringWriter();
        diagnostics.Store.WriteJson(output);
        Require(output.ToString().Contains("fixworld.mod-loading") && output.ToString().Contains("constructors_ms"), "Shared JSON presenter");
        telemetry.MarkUnavailable("missing hook");
        Require(telemetry.Snapshot.Failure == "missing hook", "Observation failure exposed");
        Console.WriteLine("PASS: " + checks + " mod loading contracts; nested/thread attribution, timing, bounds and export.");
    }
}
