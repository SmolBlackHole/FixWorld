// SPDX-License-Identifier: MPL-2.0
using System;
using System.Globalization;
using System.IO;

namespace FixWorld.Telemetry
{
    // Presentation is cold. One typed contract supplies both output formats;
    // names carry units (e.g. total_ms), and never come from hot-path formatting.
    public sealed class TelemetryWriter
    {
        private readonly TextWriter output;
        private readonly bool json;
        private bool firstRecord = true, firstValue;
        internal TelemetryWriter(TextWriter output, bool json) { this.output = output; this.json = json; }
        internal void Begin() { if (json) output.Write('['); }
        internal void End() { if (json) output.Write(']'); }
        internal void BeginRecord(string id, int version)
        {
            firstValue = true;
            if (json)
            {
                if (!firstRecord) output.Write(',');
                output.Write("{\"id\":"); Quote(id);
                output.Write(",\"schemaVersion\":"); output.Write(version.ToString(CultureInfo.InvariantCulture));
                output.Write(",\"values\":{");
            }
            else output.WriteLine("[" + id + " v" + version.ToString(CultureInfo.InvariantCulture) + "]");
            firstRecord = false;
        }
        internal void EndRecord() { if (json) output.Write("}}"); }
        public void Value(string name, long value) => Scalar(name, value.ToString(CultureInfo.InvariantCulture));
        public void Value(string name, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Scalar(name, value.ToString("R", CultureInfo.InvariantCulture));
        }
        public void Value(string name, bool value) => Scalar(name, value ? "true" : "false");
        public void Value(string name, string value)
        {
            Name(name);
            if (json) { if (value == null) output.Write("null"); else Quote(value); }
            else output.WriteLine(value ?? "null");
        }
        private void Scalar(string name, string value)
        { Name(name); output.Write(value); if (!json) output.WriteLine(); }
        private void Name(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Field name is required.", nameof(name));
            if (json)
            {
                if (!firstValue) output.Write(',');
                Quote(name); output.Write(':'); firstValue = false;
            }
            else { output.Write("  "); output.Write(name); output.Write(": "); }
        }
        private void Quote(string text)
        {
            output.Write('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') { output.Write('\\'); output.Write(c); }
                else if (c < ' ' || char.IsSurrogate(c))
                { output.Write("\\u"); output.Write(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                else output.Write(c);
            }
            output.Write('"');
        }
    }
}
