// SPDX-License-Identifier: MPL-2.0
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace FixWorld.Telemetry
{
    // The worker reads published DTOs only. It never captures providers or calls
    // Verse/Unity. Presenters must be pure and safe to run on this thread.
    public sealed class TelemetryCapture : IDisposable
    {
        private readonly object sync = new();
        private readonly Thread worker;
        private readonly TelemetryStore store;
        private readonly Action<string> report;
        private readonly int intervalMilliseconds;
        private readonly long maximumBytes;
        private readonly int processId;
        private readonly long started = Stopwatch.GetTimestamp();
        private bool stopping;
        public string Session { get; } = Guid.NewGuid().ToString("N");
        public string FilePath { get; }

        public TelemetryCapture(TelemetryStore store, string directory, Action<string> report,
            int intervalMilliseconds = 1000, long maximumBytes = 64L * 1024 * 1024)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.report = report ?? throw new ArgumentNullException(nameof(report));
            if (intervalMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds));
            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            this.intervalMilliseconds = intervalMilliseconds;
            this.maximumBytes = maximumBytes;
            using (var process = Process.GetCurrentProcess()) processId = process.Id;
            FilePath = Path.Combine(Path.GetFullPath(directory), Session + ".jsonl");
            worker = new Thread(Run) { IsBackground = true, Name = "FixWorld telemetry export" };
            worker.Start();
        }

        private void Run()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using var file = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                using var output = new StreamWriter(file, new UTF8Encoding(false));
                Report("Telemetry capture: " + FilePath);
                long sequence = 0, bytes = 0;
                while (true)
                {
                    lock (sync) { if (stopping) return; }
                    // Stage one complete record before touching the file. A broken
                    // presenter cannot splice a partial JSON object into the stream.
                    using var line = new StringWriter(CultureInfo.InvariantCulture);
                    WriteFrame(line, store, Session, processId, ++sequence,
                        (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency);
                    var text = line.ToString();
                    var length = Encoding.UTF8.GetByteCount(text) + 1L;
                    if (length > maximumBytes - bytes)
                    { Report("Telemetry capture stopped at size limit: " + FilePath); return; }
                    output.Write(text);
                    output.Write('\n');
                    output.Flush();
                    bytes += length;
                    lock (sync)
                    {
                        if (stopping) return;
                        Monitor.Wait(sync, intervalMilliseconds);
                        if (stopping) return;
                    }
                }
            }
            catch (Exception error) { Report("Telemetry capture stopped: " + error); }
        }

        internal static void WriteFrame(TextWriter output, TelemetryStore store, string session,
            int processId, long sequence, double elapsedSeconds)
        {
            // Envelope fields are transport metadata. All module data and schema
            // semantics still come exclusively from the registered presenters.
            output.Write("{\"schemaVersion\":1,\"session\":\"");
            output.Write(session);
            output.Write("\",\"processId\":"); output.Write(processId.ToString(CultureInfo.InvariantCulture));
            output.Write(",\"sequence\":"); output.Write(sequence.ToString(CultureInfo.InvariantCulture));
            output.Write(",\"utc\":\""); output.Write(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            output.Write("\",\"elapsedSeconds\":"); output.Write(elapsedSeconds.ToString("R", CultureInfo.InvariantCulture));
            output.Write(",\"records\":"); store.WriteJson(output); output.Write('}');
        }

        private void Report(string message)
        { try { report(message); } catch { /* Diagnostics must not crash the game. */ } }

        public void Dispose()
        {
            lock (sync) { stopping = true; Monitor.PulseAll(sync); }
            // A stalled disk/presenter must not hold game shutdown indefinitely.
            // Only the worker owns and closes its writer, even after this timeout.
            if (Thread.CurrentThread != worker) worker.Join(2000);
        }
    }
}
