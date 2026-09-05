// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FixWorld.Telemetry
{
    public sealed class TelemetryContract<T> where T : class
    {
        private readonly Action<T, TelemetryWriter> present;
        public TelemetryContract(string id, int schemaVersion, Action<T, TelemetryWriter> present)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", nameof(id));
            }


            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }


            Id = id; SchemaVersion = schemaVersion;
            this.present = present ?? throw new ArgumentNullException(nameof(present));
        }
        public string Id { get; }
        public int SchemaVersion { get; }
        internal void Present(T snapshot, TelemetryWriter writer)
        {
            present(snapshot, writer);
        }

    }

    // Membership mutations are cold. Readers reuse the published membership
    // list and each registration's latest immutable DTO reference.
    public sealed class TelemetryStore : IDisposable
    {
        private readonly object sync = new();
        private readonly Dictionary<string, TelemetryRegistration> registrations = new(StringComparer.Ordinal);
        private IReadOnlyList<TelemetryRegistration> view = Array.AsReadOnly(Array.Empty<TelemetryRegistration>());
        private bool disposed;
        public IReadOnlyList<TelemetryRegistration> Registrations => Volatile.Read(ref view);

        public TelemetryRegistration<T> Register<T>(TelemetryContract<T> contract) where T : class
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(TelemetryStore));
                }


                if (registrations.ContainsKey(contract.Id))
                {
                    throw new InvalidOperationException("Duplicate telemetry ID: " + contract.Id);
                }


                var registration = new TelemetryRegistration<T>(this, contract);
                registrations.Add(contract.Id, registration);
                Volatile.Write(ref view, new List<TelemetryRegistration>(registrations.Values).AsReadOnly());
                return registration;
            }
        }

        internal void Remove(TelemetryRegistration registration)
        {
            lock (sync)
            {
                registration.Deactivate();
                if (registrations.TryGetValue(registration.Id, out var current) && ReferenceEquals(registration, current))
                {
                    registrations.Remove(registration.Id);
                    Volatile.Write(ref view, new List<TelemetryRegistration>(registrations.Values).AsReadOnly());
                }
            }
        }

        public void WriteJson(TextWriter output)
        {
            Write(output, true);
        }

        public void WriteLog(TextWriter output)
        {
            Write(output, false);
        }


        private void Write(TextWriter output, bool json)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            var writer = new TelemetryWriter(output, json);
            writer.Begin();
            foreach (var registration in Registrations)
            {
                registration.Write(writer);
            }


            writer.End();
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
                foreach (var registration in registrations.Values)
                {
                    registration.Deactivate();
                }

                registrations.Clear();
                Volatile.Write(ref view, Array.AsReadOnly(Array.Empty<TelemetryRegistration>()));
            }
        }
    }

    public abstract class TelemetryRegistration : IDisposable
    {
        private readonly TelemetryStore owner;
        private readonly object sync = new();
        private object snapshot;
        private bool disposed;
        internal TelemetryRegistration(TelemetryStore owner, string id, int schema, Type snapshotType)
        { this.owner = owner; Id = id; SchemaVersion = schema; SnapshotType = snapshotType; }
        public string Id { get; }
        public int SchemaVersion { get; }
        public Type SnapshotType { get; }
        public string Generation { get; } = Guid.NewGuid().ToString("N");
        public object PublishedSnapshot => Volatile.Read(ref snapshot);
        protected void PublishSnapshot(object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(Id);
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
        internal abstract void Write(TelemetryWriter writer);
        public void Dispose()
        {
            owner.Remove(this);
            GC.SuppressFinalize(this);
        }

    }

    public sealed class TelemetryRegistration<T> : TelemetryRegistration where T : class
    {
        private readonly TelemetryContract<T> contract;
        internal TelemetryRegistration(TelemetryStore owner, TelemetryContract<T> contract)
            : base(owner, contract.Id, contract.SchemaVersion, typeof(T)) { this.contract = contract; }
        public T Snapshot => (T)PublishedSnapshot;
        public void Publish(T snapshot)
        {
            PublishSnapshot(snapshot);
        }


        internal override void Write(TelemetryWriter writer)
        {
            var current = Snapshot;
            if (current == null)
            {
                return;
            }


            writer.BeginRecord(Id, SchemaVersion, Generation);
            contract.Present(current, writer);
            writer.EndRecord();
        }
    }
}
