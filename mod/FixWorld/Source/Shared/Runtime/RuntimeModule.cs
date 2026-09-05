using System;
using System.Collections.Generic;
using FixWorld.Telemetry;

namespace FixWorld.Runtime
{
    // Implemented by the integration layer. Uninstall must also undo a partial
    // Install and quiesce callbacks before returning control to module cleanup.
    public interface IRuntimeModuleHooks
    {
        void Install();
        void Uninstall();
    }

    // Lifecycle and capture run on the runtime's owning thread. This class does
    // not serialize gameplay operations or make engine state worker-safe.
    public abstract class RuntimeModule<TSnapshot> : IDisposable where TSnapshot : class
    {
        private readonly IRuntimeModuleHooks hooks;
        private TelemetryRegistration<TSnapshot> registration;
        private ModuleState state;
        private bool initialized;
        private bool hooksAttempted;

        protected RuntimeModule(RuntimeServices services, string id, int schemaVersion,
            IRuntimeModuleHooks hooks = null)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A module ID is required.", nameof(id));
            }

            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            Id = id;
            SchemaVersion = schemaVersion;
            this.hooks = hooks;
        }

        protected RuntimeServices Services { get; }
        public string Id { get; }
        public int SchemaVersion { get; }
        public bool IsInstalled => state == ModuleState.Installed;
        public TSnapshot PublishedSnapshot => registration?.Snapshot;

        public void Install()
        {
            if (IsInstalled)
            {
                return;
            }

            if (state != ModuleState.New)
            {
                throw new InvalidOperationException("A retired or transitioning module cannot be installed.");
            }

            state = ModuleState.Installing;
            try
            {
                registration = Services.Telemetry.Register<TSnapshot>(Id, SchemaVersion);
                initialized = true;
                OnInitialize();
                registration.Publish(CaptureSnapshot());
                if (hooks != null)
                {
                    hooksAttempted = true;
                    hooks.Install();
                }
                state = ModuleState.Installed;
            }
            catch (Exception failure)
            {
                try
                { UninstallCore(); }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(failure, cleanupFailure);
                }
                throw;
            }
        }

        public TSnapshot Publish()
        {
            if (!IsInstalled)
            {
                throw new InvalidOperationException("The module is not installed.");
            }

            TSnapshot snapshot = CaptureSnapshot();
            registration.Publish(snapshot);
            return snapshot;
        }

        public void Uninstall()
        {
            if (state == ModuleState.Removed)
            {
                return;
            }

            if (state is ModuleState.Installing or ModuleState.Removing)
            {
                throw new InvalidOperationException("Module lifecycle calls cannot be reentered.");
            }

            UninstallCore();
        }

        public void Dispose() => Uninstall();

        protected virtual void OnInitialize() { }
        protected virtual void OnShutdown() { }
        protected abstract TSnapshot CaptureSnapshot();

        private void UninstallCore()
        {
            state = ModuleState.Removing;
            List<Exception> failures = null;
            try
            {
                if (hooksAttempted)
                {
                    hooks.Uninstall();
                }
            }
            catch (Exception error) { (failures ??= new()).Add(error); }
            try
            {
                if (initialized)
                {
                    OnShutdown();
                }
            }
            catch (Exception error) { (failures ??= new()).Add(error); }
            try
            { registration?.Dispose(); }
            catch (Exception error) { (failures ??= new()).Add(error); }
            state = ModuleState.Removed;
            if (failures != null)
            {
                throw new AggregateException("Module cleanup failed: " + Id, failures);
            }
        }

        private enum ModuleState { New, Installing, Installed, Removing, Removed }
    }
}
