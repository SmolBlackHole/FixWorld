// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Threading;
using FixWorld.Profiling;
using FixWorld.Telemetry;

namespace FixWorld.Caching
{
    public sealed class CacheContract<TKey, TValue>
    {
        public CacheContract(string id, int capacity, Func<TKey, TValue> create,
            IEqualityComparer<TKey> comparer = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Cache ID is required.", nameof(id));
            }


            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }


            Id = id; Capacity = capacity; Create = create ?? throw new ArgumentNullException(nameof(create));
            Comparer = comparer ?? EqualityComparer<TKey>.Default;
        }
        public string Id { get; }
        public int Capacity { get; }
        internal Func<TKey, TValue> Create { get; }
        internal IEqualityComparer<TKey> Comparer { get; }
    }

    // Main-thread caches share policy and diagnostics, not a global dictionary
    // of object values. Each consumer resolves and retains its typed handle once.
    public sealed class CacheStore : IDisposable
    {
        private int ownerThread;
        private long generation;
        private readonly Dictionary<string, CacheRegistration> caches = new(StringComparer.Ordinal);
        private readonly Profiler<ProfileKey> profiler;
        private readonly TelemetryRegistration<CacheStoreSnapshot> telemetry;
        private bool disposed;
        private int activeFactories;
        public CacheStore(TelemetryStore telemetry, Profiler<ProfileKey> profiler)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }


            this.profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
            this.telemetry = telemetry.Register(CacheStoreSnapshot.Contract);
        }
        public CacheStoreSnapshot Snapshot => telemetry.Snapshot;
        internal long Generation => Interlocked.Read(ref generation);

        public void BindCurrentThread()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(CacheStore));
            }


            int current = Thread.CurrentThread.ManagedThreadId;
            int existing = Interlocked.CompareExchange(ref ownerThread, current, 0);
            if (existing != 0 && existing != current)
            {
                throw new InvalidOperationException("Cache store is bound to another thread.");
            }

        }

        public TypedCache<TKey, TValue> Create<TKey, TValue>(CacheContract<TKey, TValue> contract)
        {
            AssertSetupAccess();
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            if (caches.ContainsKey(contract.Id))
            {
                throw new InvalidOperationException("Duplicate cache ID: " + contract.Id);
            }


            var created = new TypedCache<TKey, TValue>(this, contract,
                profiler.GetSlot(new ProfileKey(contract.Id, "cache.create")));
            caches.Add(contract.Id, created);
            return created;
        }
        public void Publish()
        {
            AssertAccess();
            var values = new CacheStatistics[caches.Count];
            int index = 0;
            foreach (var cache in caches.Values)
            {
                values[index++] = cache.Capture();
            }


            telemetry.Publish(new CacheStoreSnapshot(Array.AsReadOnly(values)));
        }
        // Safe during background def reload. Values are discarded lazily on
        // their owning thread, before the next read or statistics publication.
        public void InvalidateAll()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(CacheStore));
            }


            Interlocked.Increment(ref generation);
        }
        internal void Remove(CacheRegistration cache)
        {
            AssertSetupAccess();
            if (caches.TryGetValue(cache.Id, out var current) && ReferenceEquals(cache, current))
            {
                caches.Remove(cache.Id);
            }

        }
        internal void AssertAccess()
        {
            if (ownerThread == 0)
            {
                throw new InvalidOperationException("Cache store has not been bound to its owner thread.");
            }


            AssertSetupAccess();
        }
        internal void AssertSetupAccess()
        {
            if (ownerThread != 0 && Thread.CurrentThread.ManagedThreadId != ownerThread)
            {
                throw new InvalidOperationException("Cache access requires its owner thread.");
            }


            if (disposed)
            {
                throw new ObjectDisposedException(nameof(CacheStore));
            }

        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }


            AssertSetupAccess();
            if (activeFactories != 0)
            {
                throw new InvalidOperationException("Cache factories must finish before the store is disposed.");
            }


            foreach (var cache in caches.Values)
            {
                cache.Retire();
            }

            caches.Clear();
            telemetry.Dispose();
            disposed = true;
        }
        internal void BeginFactory() => activeFactories++;
        internal void EndFactory() => activeFactories--;
    }

    public abstract class CacheRegistration : IDisposable
    {
        protected CacheRegistration(string id) { Id = id; }
        public string Id { get; }
        public abstract void Clear();
        public abstract void Dispose();
        internal abstract void Retire();
        internal abstract CacheStatistics Capture();
    }

    public sealed class TypedCache<TKey, TValue> : CacheRegistration
    {
        private readonly CacheStore owner;
        private readonly CacheContract<TKey, TValue> contract;
        private readonly ProfileSlot<ProfileKey> createProfile;
        private readonly Dictionary<TKey, Entry> entries;
        private readonly LinkedList<TKey> insertionOrder = new();
        private long hits, misses, evictions, invalidations;
        private bool disposed, creating;
        private long generation;

        internal TypedCache(CacheStore owner, CacheContract<TKey, TValue> contract, ProfileSlot<ProfileKey> createProfile)
            : base(contract.Id)
        {
            this.owner = owner; this.contract = contract; this.createProfile = createProfile;
            entries = new Dictionary<TKey, Entry>(contract.Comparer);
            generation = owner.Generation;
        }
        public int Count { get { AssertAccess(); return entries.Count; } }
        public bool TryGet(TKey key, out TValue value)
        {
            AssertAccess();
            if (entries.TryGetValue(key, out var entry)) { hits++; value = entry.Value; return true; }
            misses++; value = default; return false;
        }
        public TValue GetOrAdd(TKey key)
        {
            if (TryGet(key, out var found))
            {
                return found;
            }


            AssertNotCreating();
            TValue value;
            creating = true;
            owner.BeginFactory();
            using (var measurement = createProfile.Measure())
            {
                try { value = contract.Create(key); }
                catch { measurement.Fail(); throw; }
                finally { creating = false; owner.EndFactory(); }
            }
            if (entries.Count == contract.Capacity)
            {
                entries.Remove(insertionOrder.First.Value);
                insertionOrder.RemoveFirst();
                evictions++;
            }
            entries.Add(key, new Entry(value, insertionOrder.AddLast(key)));
            return value;
        }
        public bool Invalidate(TKey key)
        {
            AssertAccess(); AssertNotCreating();
            if (!entries.TryGetValue(key, out var entry))
            {
                return false;
            }


            entries.Remove(key); insertionOrder.Remove(entry.Node); invalidations++; return true;
        }
        public override void Clear()
        {
            AssertAccess(); AssertNotCreating();
            ClearValues();
        }
        public override void Dispose()
        {
            if (disposed)
            {
                return;
            }


            owner.AssertSetupAccess(); Retire(); owner.Remove(this);
        }
        internal override void Retire()
        { AssertNotCreating(); entries.Clear(); insertionOrder.Clear(); disposed = true; }
        internal override CacheStatistics Capture()
        {
            AssertAccess();
            return new(Id, entries.Count, contract.Capacity, hits, misses, evictions, invalidations);
        }
        private void AssertAccess()
        {
            owner.AssertAccess(); if (disposed)
            {
                throw new ObjectDisposedException(Id);
            }


            long current = owner.Generation;
            if (generation != current)
            {
                AssertNotCreating(); ClearValues(); generation = current;
            }
        }
        private void ClearValues()
        { invalidations += entries.Count; entries.Clear(); insertionOrder.Clear(); }
        private void AssertNotCreating()
        {
            if (creating)
            {
                throw new InvalidOperationException("A cache factory cannot mutate or recursively fill its own cache.");
            }
        }
        private readonly struct Entry
        {
            public Entry(TValue value, LinkedListNode<TKey> node) { Value = value; Node = node; }
            public TValue Value { get; }
            public LinkedListNode<TKey> Node { get; }
        }
    }

    public readonly struct CacheStatistics
    {
        internal CacheStatistics(string id, int count, int capacity, long hits, long misses, long evictions, long invalidations)
        { Id = id; Count = count; Capacity = capacity; Hits = hits; Misses = misses; Evictions = evictions; Invalidations = invalidations; }
        public string Id { get; }
        public int Count { get; }
        public int Capacity { get; }
        public long Hits { get; }
        public long Misses { get; }
        public long Evictions { get; }
        public long Invalidations { get; }
    }

    public sealed class CacheStoreSnapshot
    {
        internal CacheStoreSnapshot(IReadOnlyList<CacheStatistics> caches) { Caches = caches; }
        public IReadOnlyList<CacheStatistics> Caches { get; }
        public static TelemetryContract<CacheStoreSnapshot> Contract { get; } = new("fixworld.caches", 1, (data, writer) =>
        {
            writer.Value("cache_count", data.Caches.Count);
            for (int i = 0; i < data.Caches.Count; i++)
            {
                var cache = data.Caches[i]; var prefix = i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                writer.Value(prefix + "id", cache.Id); writer.Value(prefix + "entries", cache.Count);
                writer.Value(prefix + "capacity", cache.Capacity); writer.Counter(prefix + "hits", cache.Hits);
                writer.Counter(prefix + "misses", cache.Misses); writer.Counter(prefix + "evictions", cache.Evictions);
                writer.Counter(prefix + "invalidations", cache.Invalidations);
            }
        });
    }
}
