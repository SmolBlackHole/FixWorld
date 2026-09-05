// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FixWorld.Profiling;

namespace FixWorld.Telemetry
{
    internal enum ModLoadPart { Assemblies, Content, Xml, Constructors }
    internal sealed class ModLoadSnapshot
    {
        internal ModLoadSnapshot(string id, string name, double[] times, long errors, long warnings, string[] messages)
        { Id = id; Name = name; Times = Array.AsReadOnly(times); Errors = errors; Warnings = warnings; Messages = Array.AsReadOnly(messages); }
        internal string Id { get; }
        internal string Name { get; }
        internal IReadOnlyList<double> Times { get; }
        internal double TotalMilliseconds => Times.Sum();
        internal long Errors { get; }
        internal long Warnings { get; }
        internal IReadOnlyList<string> Messages { get; }
    }
    internal sealed class ModLoadingSnapshot
    {
        internal ModLoadingSnapshot(ModLoadSnapshot[] mods, string failure) { Mods = Array.AsReadOnly(mods); Failure = failure; }
        internal IReadOnlyList<ModLoadSnapshot> Mods { get; }
        internal string Failure { get; }
    }
    // Loading-only attribution. No game objects, stack inspection or log parsing.
    internal sealed class ModLoadingTelemetry : IDisposable
    {
        internal sealed class Entry
        {
            internal string Id, Name;
            internal readonly long[] Ticks = new long[4];
            internal readonly ProfileSlot<ProfileKey>[] Slots = new ProfileSlot<ProfileKey>[4];
            internal readonly List<string> Messages = new();
            internal long Errors, Warnings;
        }
        [ThreadStatic] private static Scope current;
        private readonly object sync = new();
        private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        private readonly TelemetryRegistration<ModLoadingSnapshot> registration;
        private readonly Profiler<ProfileKey> profiler;
        private readonly Func<long> clock;
        private readonly long frequency;
        private bool dirty;
        private string failure;
        internal void MarkUnavailable(string message) { lock (sync) { failure = message; dirty = true; } Publish(); }
        internal ModLoadingSnapshot Snapshot => registration.Snapshot;
        internal ModLoadingTelemetry(LibraryDiagnostics diagnostics, Func<long> clock = null, long frequency = 0)
        {
            profiler = diagnostics.Profiler;
            this.clock = clock ?? Stopwatch.GetTimestamp;
            this.frequency = frequency == 0 ? Stopwatch.Frequency : frequency;
            registration = diagnostics.Store.Register(new TelemetryContract<ModLoadingSnapshot>("fixworld.mod-loading", 1, Present));
        }
        internal Scope Begin(string id, string name, ModLoadPart? part)
        {
            id = string.IsNullOrEmpty(id) ? "unattributed" : id;
            lock (sync)
            {
                if (!entries.TryGetValue(id, out var entry))
                {
                    entry = new Entry { Id = id, Name = string.IsNullOrEmpty(name) ? id : name };
                    entries.Add(id, entry);
                    dirty = true;
                }
                if (part.HasValue && entry.Slots[(int)part.Value] == null)
                    entry.Slots[(int)part.Value] = profiler.GetSlot(new ProfileKey(id, "Load." + part.Value, ProfileSource.RimWorld));
                return new Scope(this, entry, part);
            }
        }
        internal void RecordMessage(string text, bool error)
        {
            var scope = current;
            lock (sync)
            {
                Entry entry;
                if (scope?.owner == this)
                    entry = scope.entry;
                else
                {
                    if (!entries.TryGetValue("unattributed", out entry))
                        entries.Add("unattributed", entry = new Entry { Id = "unattributed", Name = "Unattributed loading messages" });
                }
                if (error)
                    entry.Errors++;
                else
                    entry.Warnings++;
                if (entry.Messages.Count < 5)
                {
                    string sample = (error ? "Error: " : "Warning: ") + (text ?? "");
                    if (sample.Length > 2048)
                        sample = sample.Substring(0, 2048) + "...";
                    if (!entry.Messages.Contains(sample))
                        entry.Messages.Add(sample);
                }
                dirty = true;
            }
        }
        internal void Publish()
        {
            lock (sync)
            {
                if (!dirty)
                    return;
                var rows = entries.Values.Select(e => new ModLoadSnapshot(e.Id, e.Name,
                    e.Ticks.Select(t => t * 1000d / frequency).ToArray(), e.Errors, e.Warnings, e.Messages.ToArray()))
                    .OrderByDescending(e => e.TotalMilliseconds).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
                registration.Publish(new ModLoadingSnapshot(rows, failure));
                dirty = false;
            }
        }
        public void Dispose() { registration.Dispose(); }
        internal sealed class Scope : IDisposable
        {
            internal readonly ModLoadingTelemetry owner;
            internal readonly Entry entry;
            private readonly ModLoadPart? part;
            private readonly Scope previous;
            private readonly long started;
            private bool disposed;
            private bool failed;
            internal void Fail(Exception error) { failed = true; owner.RecordMessage(error.ToString(), true); }
            internal Scope(ModLoadingTelemetry owner, Entry entry, ModLoadPart? part)
            { this.owner = owner; this.entry = entry; this.part = part; previous = current; current = this; started = owner.clock(); }
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                if (current == this)
                    current = previous;
                if (!part.HasValue)
                    return;
                long elapsed = Math.Max(0, owner.clock() - started);
                lock (owner.sync)
                {
                    entry.Ticks[(int)part.Value] += elapsed;
                    owner.dirty = true;
                    // The production clock is Stopwatch. Injected clocks keep tests deterministic.
                    entry.Slots[(int)part.Value].ObserveStopwatchTicks(
                        (long)((double)elapsed * Stopwatch.Frequency / owner.frequency), !failed);
                }
            }
        }
        private static void Present(ModLoadingSnapshot data, TelemetryWriter writer)
        {
            writer.Value("observation_failure", data.Failure);
            for (int i = 0; i < data.Mods.Count; i++)
            {
                var mod = data.Mods[i];
                string p = "mods." + i + ".";
                writer.Value(p + "id", mod.Id);
                writer.Value(p + "name", mod.Name);
                writer.Value(p + "observed_ms", mod.TotalMilliseconds);
                for (int j = 0; j < 4; j++)
                    writer.Value(p + ((ModLoadPart)j).ToString().ToLowerInvariant() + "_ms", mod.Times[j]);
                writer.Value(p + "errors", mod.Errors);
                writer.Value(p + "warnings", mod.Warnings);
                for (int j = 0; j < mod.Messages.Count; j++)
                    writer.Value(p + "messages." + j, mod.Messages[j]);
            }
        }
    }
}
