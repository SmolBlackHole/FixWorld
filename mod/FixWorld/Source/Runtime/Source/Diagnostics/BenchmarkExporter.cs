using System;
using System.Runtime.Serialization.Json;
using FixWorld.Runtime;
using Verse;

namespace FixWorld.Diagnostics
{
    internal static class BenchmarkExporter
    {
        private const string OutputEnvironmentVariable =
            "FIXWORLD_BENCHMARK_OUTPUT";

        private static readonly object CompletionSync = new object();
        private static readonly string OutputPath =
            Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
        private static bool reportWritten;

        internal static bool Enabled => !string.IsNullOrWhiteSpace(OutputPath);

        internal static void Write(RuntimeDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (!Enabled)
            {
                return;
            }

            lock (CompletionSync)
            {
                if (reportWritten)
                {
                    return;
                }

                reportWritten = true;
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(
                        typeof(RuntimeDiagnosticsSnapshot));
                AtomicFile.Write(
                    OutputPath,
                    stream => serializer.WriteObject(stream, snapshot));
                Log.Message(
                    "[FixWorld] Benchmark report written: " + OutputPath);
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FixWorld] Could not write benchmark report: " +
                    exception);
            }
        }
    }
}
