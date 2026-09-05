using System;
using System.Collections.Generic;
using System.Threading;

namespace FixWorld.Telemetry
{
    // Registry changes are cold. Recording uses module-owned profiler slots;
    // published DTO reads never acquire the registry lock.
    public sealed class TelemetryStore : IDisposable
    {
        private readonly object sync = new();
        private readonly Dictionary<string, TelemetryRegistration> registrations =
            new(StringComparer.Ordinal);
        private IReadOnlyList<TelemetryRegistration> publishedRegistrations = [];
        private bool disposed;

        public IReadOnlyList<TelemetryRegistration> Registrations =>
            Volatile.Read(ref publishedRegistrations);

        public TelemetryRegistration<TSnapshot> Register<TSnapshot>(
            string id, int schemaVersion) where TSnapshot : class
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A telemetry ID is required.", nameof(id));
            }

            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(TelemetryStore));
                }

                if (registrations.ContainsKey(id))
                {
                    throw new InvalidOperationException("Telemetry ID is already registered: " + id);
                }

                var registration = new TelemetryRegistration<TSnapshot>(this, id, schemaVersion);
                registrations.Add(id, registration);
                PublishRegistrations();
                return registration;
            }
        }

        internal void Unregister(TelemetryRegistration registration)
        {
            lock (sync)
            {
                registration.Deactivate();
                if (registrations.TryGetValue(registration.Id, out TelemetryRegistration current) &&
                    ReferenceEquals(current, registration))
                {
                    registrations.Remove(registration.Id);
                    PublishRegistrations();
                }
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                foreach (TelemetryRegistration registration in registrations.Values)
                {
                    registration.Deactivate();
                }

                registrations.Clear();
                Volatile.Write(ref publishedRegistrations, []);
            }
        }

        private void PublishRegistrations() =>
            Volatile.Write(ref publishedRegistrations,
                new List<TelemetryRegistration>(registrations.Values).AsReadOnly());
    }

    // Metadata and the latest snapshot are shared with cold presentation code.
    // DTO immutability is a provider contract, not a deep copy performed here.
    public abstract class TelemetryRegistration : IDisposable
    {
        private readonly TelemetryStore owner;
        private readonly object sync = new();
        private object snapshot;
        private bool disposed;

        internal TelemetryRegistration(TelemetryStore owner, string id,
            int schemaVersion, Type snapshotType)
        {
            this.owner = owner;
            Id = id;
            SchemaVersion = schemaVersion;
            SnapshotType = snapshotType;
        }

        public string Id { get; }
        public int SchemaVersion { get; }
        public Type SnapshotType { get; }
        public object PublishedSnapshot => Volatile.Read(ref snapshot);

        protected void PublishSnapshot(object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(TelemetryRegistration));
                }

                Volatile.Write(ref snapshot, value);
            }
        }

        internal void Deactivate()
        {
            lock (sync)
            {
                disposed = true;
            }
        }

        public void Dispose() => owner.Unregister(this);
    }

    public sealed class TelemetryRegistration<TSnapshot> : TelemetryRegistration
        where TSnapshot : class
    {
        internal TelemetryRegistration(TelemetryStore owner, string id, int schemaVersion)
            : base(owner, id, schemaVersion, typeof(TSnapshot)) { }

        public TSnapshot Snapshot => (TSnapshot)PublishedSnapshot;
        public void Publish(TSnapshot snapshot) => PublishSnapshot(snapshot);
    }
}
