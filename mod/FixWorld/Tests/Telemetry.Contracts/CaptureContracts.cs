// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Web.Script.Serialization;
using FixWorld.Telemetry;

internal static class CaptureContracts
{
    private sealed class FinalizingRegistration : TelemetryRegistration
    {
        internal static int Finalized;
        internal FinalizingRegistration(TelemetryStore owner) : base(owner, "finalizer", 1, typeof(object)) { }
        ~FinalizingRegistration() { Interlocked.Increment(ref Finalized); }
        internal override void Write(TelemetryWriter writer) { }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DisposeDerived(TelemetryStore store)
    {
        var registration = new FinalizingRegistration(store);
        registration.Dispose();
        return new WeakReference(registration);
    }
    private sealed class Data
    {
        public Data(long count = 7) { Count = count; }
        public long Count { get; }
    }
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FixWorld-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new TelemetryStore();
            var retired = DisposeDerived(store);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!retired.IsAlive && FinalizingRegistration.Finalized == 0, "Base Dispose suppresses derived finalizers");
            int thread = 0;
            var contract = new TelemetryContract<Data>("arbitrary.module", 17, (data, writer) =>
            {
                thread = Thread.CurrentThread.ManagedThreadId;
                writer.Counter("requests", data.Count); writer.Value("gauge", 1.5);
                writer.Value("text", "one\ntwo\"\\");
            });
            var registration = store.Register(contract);
            var value = new Data();
            registration.Publish(value);
            var oldCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                using var output = new StringWriter();
                TelemetryCapture.WriteFrame(output, store, "session", 123, 1, 1.25);
                var frame = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(output.ToString());
                var record = (Dictionary<string, object>)((System.Collections.ArrayList)frame["records"])[0];
                var counters = (System.Collections.ArrayList)record["counters"];
                Check((string)record["id"] == contract.Id && (int)record["schemaVersion"] == 17
                    && (string)record["generation"] == registration.Generation
                    && counters.Count == 1 && (string)counters[0] == "requests", "Contract metadata survives generic envelope");
                Check(Convert.ToDouble(frame["elapsedSeconds"]) == 1.25 && ReferenceEquals(registration.Snapshot, value),
                    "Culture invariant transport and no DTO copy");
                // Optional cross-language fixture uses the actual production presenter.
                var fixture = Environment.GetEnvironmentVariable("FIXWORLD_CAPTURE_TEST_OUTPUT");
                if (!string.IsNullOrEmpty(fixture))
                {
                    registration.Publish(new Data(17));
                    using var next = new StringWriter();
                    TelemetryCapture.WriteFrame(next, store, "session", 123, 2, 2.25);
                    File.WriteAllText(fixture, output + "\n" + next + "\n");
                    registration.Publish(value);
                }
            }
            finally { Thread.CurrentThread.CurrentCulture = oldCulture; }
            var generation = registration.Generation;
            registration.Dispose();
            registration = store.Register(contract); registration.Publish(value);
            Check(registration.Generation != generation, "Replacement has a new counter lifetime");

            using (var written = new ManualResetEventSlim())
            {
                var capture = new TelemetryCapture(store, directory, _ => written.Set(), 20);
                Check(written.Wait(3000), "Background capture starts");
                Check(SpinWait.SpinUntil(() => CompleteLines(capture.FilePath) >= 2, 3000), "Capture flushes live complete JSONL lines");
                capture.Dispose(); capture.Dispose();
                var lines = File.ReadAllLines(capture.FilePath);
                Check(lines.Length >= 2 && thread != Thread.CurrentThread.ManagedThreadId, "Presenter runs off the producer thread");
                long length = new FileInfo(capture.FilePath).Length;
                Thread.Sleep(60);
                Check(new FileInfo(capture.FilePath).Length == length, "Disposal stops export");
            }
            using (var failed = new ManualResetEventSlim())
            {
                using var capture = new TelemetryCapture(store, directory, message =>
                { if (message.Contains("size limit")) failed.Set(); }, 10, 1);
                Check(failed.Wait(3000), "File size bound stops exporter");
                Check(new FileInfo(capture.FilePath).Length == 0, "Limit never writes half a record");
            }
            string notDirectory = Path.Combine(directory, "file"); File.WriteAllText(notDirectory, "x");
            using (var failed = new ManualResetEventSlim())
            {
                using var capture = new TelemetryCapture(store, notDirectory, message => failed.Set());
                Check(failed.Wait(3000), "Unwritable directory failure is isolated");
            }
            using (var bad = store.Register(new TelemetryContract<Data>("broken", 1, (d, w) => throw new Exception("fixture"))))
            using (var failed = new ManualResetEventSlim())
            {
                bad.Publish(value);
                using var capture = new TelemetryCapture(store, directory, message =>
                { if (message.Contains("stopped:")) failed.Set(); });
                Check(failed.Wait(3000), "Bad presenter cannot crash worker/process");
                Check(new FileInfo(capture.FilePath).Length == 0, "Bad presenter leaves no partial JSON record");
            }
            Console.WriteLine("PASS: 13 capture contract checks, including generic production export and finalizer suppression.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static int CompleteLines(string path)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(input);
            int count = 0; foreach (char c in reader.ReadToEnd()) if (c == '\n') count++;
            return count;
        }
        catch (IOException) { return 0; }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
