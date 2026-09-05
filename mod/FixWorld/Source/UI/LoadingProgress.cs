// SPDX-License-Identifier: MPL-2.0
using System;
using System.Diagnostics;
using System.Threading;
using FixWorld.Telemetry;

namespace FixWorld.UI
{
    internal enum LoadingStage { Reset, Mods, Content, Classes, Xml, Import, Bind, PreImplied, CrossReferences, Resolve, PostImplied, FinalizeDefs, Runtime, Deferred, Complete }

    // One source for the overlay, diagnostics and JSON. No Unity objects in the contract.
    internal sealed class LoadingProgress : IDisposable
    {
        internal static readonly string[] Names = ["Reset play data", "Initialize mods", "Prepare mod content", "Create mod classes", "Load and patch XML", "Import definitions", "Early binding", "Generate pre-resolve definitions", "Resolve cross-references", "Resolve definitions", "Generate post-resolve definitions", "Finalize definitions", "Initialize runtime", "Execute deferred main-thread work", "Complete"];
        internal static readonly string[] ShortNames = ["Reset", "Mods", "Content", "Classes", "XML", "Import", "Bind", "Pre-implied", "Cross refs", "Resolve", "Post-implied", "Defs done", "Runtime", "Deferred", "Ready"];
        internal static readonly int[] GroupStarts = [0, 2, 4, 12, 15];
        internal static readonly string[] Groups = ["Boot", "Content", "Definitions", "Finalize"];
        private readonly object sync = new();
        private readonly TelemetryRegistration<LoadingSnapshot> registration;
        private LoadingSnapshot current;
        private long transitionAt;
        private readonly double[] durations = new double[Names.Length];
        private readonly Func<long> timestamp;
        private readonly long frequency;
        internal LoadingSnapshot Current => Volatile.Read(ref current);
        internal LoadingProgress(TelemetryStore store, Func<long> timestamp = null, long frequency = 0)
        {
            this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
            this.frequency = frequency == 0 ? Stopwatch.Frequency : frequency;
            registration = store.Register(new TelemetryContract<LoadingSnapshot>("fixworld.loading", 1, (data, writer) =>
            {
                writer.Value("active", data.Active);
                writer.Value("stage", Names[(int)data.Stage]);
                writer.Value("elapsed_at_transition_ms", data.ElapsedMilliseconds);
                writer.Value("failure", data.Failure);
                for (int i = 0; i < Names.Length; i++)
                {
                    writer.Value(ShortNames[i] + "_ms", data.Duration(i));
                }
            }));
        }
        internal void Begin()
        {
            lock (sync)
            {
                Array.Clear(durations, 0, durations.Length);
                transitionAt = timestamp();
                Publish(new LoadingSnapshot(LoadingStage.Reset, true, transitionAt, 0, "", durations));
            }
        }
        internal bool Transition(LoadingStage stage)
        {
            lock (sync)
            {
                var before = current;
                if (before == null || !before.Active || stage <= before.Stage)
                {
                    return false;
                }

                Set(stage, stage != LoadingStage.Complete, "");
                return true;
            }
        }
        internal void Fail(Exception error)
        {
            lock (sync)
            { if (current?.Active == true)
                {
                    Set(current.Stage, false, error.Message);
                }
            }
        }
        internal void CrossReferences(bool earlyBinding)
        {
            // Mods also resolve their own XML while constructing mod classes.
            // That is not the global Def-binding phase after the XML import.
            lock (sync)
            {
                if (current?.Active == true && current.Stage >= LoadingStage.Import)
                {
                    Transition(earlyBinding ? LoadingStage.Bind : LoadingStage.CrossReferences);
                }
            }
        }
        private void Set(LoadingStage stage, bool active, string failure)
        {
            long now = timestamp();
            durations[(int)current.Stage] += Math.Max(0, now - transitionAt) * 1000.0 / frequency;
            transitionAt = now;
            Publish(new LoadingSnapshot(stage, active, current.StartedAt, Math.Max(0, now - current.StartedAt) * 1000.0 / frequency, failure, durations));
        }
        private void Publish(LoadingSnapshot snapshot) { Volatile.Write(ref current, snapshot); registration.Publish(snapshot); }
        internal double Elapsed(LoadingSnapshot snapshot) => snapshot.Active
            ? Math.Max(0, timestamp() - snapshot.StartedAt) * 1000.0 / frequency : snapshot.ElapsedMilliseconds;
        internal static int Group(LoadingStage stage) => stage < LoadingStage.Content ? 0 : stage < LoadingStage.Xml ? 1 : stage < LoadingStage.Runtime ? 2 : 3;
        public void Dispose() => registration.Dispose();
    }
    internal sealed class LoadingSnapshot
    {
        private readonly double[] durations;
        internal LoadingSnapshot(LoadingStage stage, bool active, long startedAt, double elapsed, string failure, double[] durations)
        { Stage = stage; Active = active; StartedAt = startedAt; ElapsedMilliseconds = elapsed; Failure = failure; this.durations = (double[])durations.Clone(); }
        internal LoadingStage Stage { get; }
        internal bool Active { get; }
        internal long StartedAt { get; }
        internal double ElapsedMilliseconds { get; }
        internal string Failure { get; }
        internal double Duration(int index) => durations[index];
    }
}
