// SPDX-License-Identifier: MPL-2.0
using System;
using System.Reflection;
using System.Threading;

namespace FixWorld.Bootstrap
{
    public enum BootPhase { Cold, Waiting, Starting, CoreReady, Attaching, Attached, Completing, Ready, RestartPending, Disabled, Failed, Stopped }

    // Functional state, not telemetry. Only one canonical Bootstrap assembly owns it.
    public sealed class BootSession
    {
        public static BootSession Current { get; } = new();
        private readonly object sync = new();
        private volatile BootPhase phase;
        private Assembly coreAssembly;
        private object attachment;
        private string failure;
        public BootPhase Phase => phase;
        public string Failure => Volatile.Read(ref failure);
        public bool IsAttached { get { var current = phase; return current == BootPhase.Attached || current == BootPhase.Completing || current == BootPhase.Ready; } }

        public bool Enter(bool active)
        {
            lock (sync)
            {
                if (phase != BootPhase.Cold)
                    return false;
                phase = active ? BootPhase.Waiting : BootPhase.Disabled;
                return active;
            }
        }

        public void StartCore(Assembly assembly, Action initialize)
        {
            lock (sync)
            {
                if (coreAssembly != null && !ReferenceEquals(coreAssembly, assembly))
                    throw new InvalidOperationException("A different FixWorld assembly attempted to start the core.");
                if (phase == BootPhase.CoreReady || IsAttached)
                    return;
                Require(BootPhase.Waiting);
                coreAssembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
                Run(BootPhase.Starting, BootPhase.CoreReady, initialize);
            }
        }

        public void Attach(Assembly assembly, object owner, Action initialize)
        {
            lock (sync)
            {
                if (!ReferenceEquals(coreAssembly, assembly))
                    throw new InvalidOperationException("Preloader and mod resolved different FixWorld assemblies.");
                if (owner == null)
                    throw new ArgumentNullException(nameof(owner));
                if (IsAttached)
                {
                    if (!ReferenceEquals(attachment, owner))
                        throw new InvalidOperationException("FixWorld already has a different ModContentPack.");
                    return;
                }
                Require(BootPhase.CoreReady);
                attachment = owner;
                Run(BootPhase.Attaching, BootPhase.Attached, initialize);
            }
        }

        public void Complete(Action initialize)
        {
            lock (sync)
            {
                if (phase == BootPhase.Ready)
                    return;
                Require(BootPhase.Completing);
                Run(BootPhase.Completing, BootPhase.Ready, initialize);
            }
        }

        public void BeginCompletion(Action schedule)
        {
            lock (sync)
            {
                if (phase == BootPhase.Completing || phase == BootPhase.Ready)
                    return;
                Require(BootPhase.Attached);
                phase = BootPhase.Completing;
                try
                { schedule(); }
                catch (Exception error) { Fail(error); throw; }
            }
        }

        public void RestartPending()
        {
            lock (sync)
            { Require(BootPhase.Cold); phase = BootPhase.RestartPending; }
        }
        public void Fail(Exception error) { lock (sync) { failure = error.ToString(); phase = BootPhase.Failed; } }
        public void Stop() { lock (sync) phase = BootPhase.Stopped; }
        private void Require(BootPhase expected)
        {
            if (phase != expected)
                throw new InvalidOperationException($"Bootstrap phase {phase}, expected {expected}.");
        }
        private void Run(BootPhase working, BootPhase completed, Action initialize)
        {
            phase = working;
            try
            { initialize(); phase = completed; }
            catch (Exception error) { Fail(error); throw; }
        }
    }
}
