using System;
using System.Collections.Generic;
using System.Threading;

namespace FixWorld.Caching
{
    internal sealed class CacheSnapshot<TKey, TValue, TStamp>
    {
        private readonly Dictionary<TKey, CacheEntry<TValue, TStamp>> entries;

        internal CacheSnapshot(
            long generation,
            IDictionary<TKey, CacheEntry<TValue, TStamp>> entries,
            IEqualityComparer<TKey> keyComparer,
            bool takeOwnership = false)
        {
            if (generation < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }

            Generation = generation;
            this.entries = takeOwnership &&
                           entries is Dictionary<
                               TKey,
                               CacheEntry<TValue, TStamp>> owned
                ? owned
                : entries == null
                    ? new Dictionary<TKey, CacheEntry<TValue, TStamp>>(keyComparer)
                    : new Dictionary<TKey, CacheEntry<TValue, TStamp>>(
                        entries,
                        keyComparer);
        }

        internal long Generation { get; }

        internal int Count => entries.Count;

        internal CacheLookup<TValue, TStamp> Lookup(
            TKey key,
            Func<TStamp, bool> isFresh = null)
        {
            try
            {
                if (!entries.TryGetValue(key, out CacheEntry<TValue, TStamp> entry))
                {
                    return CacheLookup<TValue, TStamp>.Miss();
                }

                return isFresh == null || isFresh(entry.Stamp)
                    ? CacheLookup<TValue, TStamp>.Hit(entry)
                    : CacheLookup<TValue, TStamp>.Stale(entry);
            }
            catch (Exception exception)
            {
                return CacheLookup<TValue, TStamp>.Failed(exception);
            }
        }

        internal IEnumerable<KeyValuePair<TKey, CacheEntry<TValue, TStamp>>>
            Enumerate()
        {
            foreach (KeyValuePair<TKey, CacheEntry<TValue, TStamp>> pair in entries)
            {
                yield return pair;
            }
        }

        internal Dictionary<TKey, CacheEntry<TValue, TStamp>> CopyEntries(
            IEqualityComparer<TKey> keyComparer)
        {
            return new Dictionary<TKey, CacheEntry<TValue, TStamp>>(
                entries,
                keyComparer);
        }
    }

    internal sealed class CacheDelta<TKey, TValue, TStamp>
    {
        private readonly IReadOnlyList<CacheMutation<TKey, TValue, TStamp>>
            mutations;

        internal CacheDelta(
            IReadOnlyList<CacheMutation<TKey, TValue, TStamp>> mutations)
        {
            this.mutations = mutations ??
                throw new ArgumentNullException(nameof(mutations));
        }

        internal int Count => mutations.Count;

        internal IReadOnlyList<CacheMutation<TKey, TValue, TStamp>> Mutations =>
            mutations;
    }

    internal sealed class CacheDeltaBuilder<TKey, TValue, TStamp>
    {
        private readonly List<CacheMutation<TKey, TValue, TStamp>> mutations =
            new List<CacheMutation<TKey, TValue, TStamp>>();

        internal int Count => mutations.Count;

        internal void Upsert(TKey key, TValue value, TStamp stamp)
        {
            mutations.Add(
                CacheMutation<TKey, TValue, TStamp>.Upsert(key, value, stamp));
        }

        internal void Remove(TKey key)
        {
            mutations.Add(CacheMutation<TKey, TValue, TStamp>.Remove(key));
        }

        internal CacheDelta<TKey, TValue, TStamp> Build()
        {
            return new CacheDelta<TKey, TValue, TStamp>(mutations.ToArray());
        }
    }

    internal sealed class CacheRuntime<TKey, TValue, TStamp>
    {
        private readonly object writerSync = new object();
        private readonly IEqualityComparer<TKey> keyComparer;
        private CacheSnapshot<TKey, TValue, TStamp> snapshot;

        internal CacheRuntime(
            IDictionary<TKey, CacheEntry<TValue, TStamp>> initialEntries = null,
            IEqualityComparer<TKey> keyComparer = null)
        {
            this.keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            snapshot = new CacheSnapshot<TKey, TValue, TStamp>(
                0L,
                initialEntries,
                this.keyComparer);
        }

        internal CacheSnapshot<TKey, TValue, TStamp> Snapshot =>
            Volatile.Read(ref snapshot);

        internal CacheSnapshot<TKey, TValue, TStamp> Publish(
            CacheDelta<TKey, TValue, TStamp> delta)
        {
            if (delta == null)
            {
                throw new ArgumentNullException(nameof(delta));
            }

            lock (writerSync)
            {
                CacheSnapshot<TKey, TValue, TStamp> current = snapshot;
                Dictionary<TKey, CacheEntry<TValue, TStamp>> nextEntries =
                    current.CopyEntries(keyComparer);
                foreach (CacheMutation<TKey, TValue, TStamp> mutation in
                         delta.Mutations)
                {
                    switch (mutation.Kind)
                    {
                        case CacheMutationKind.Upsert:
                            nextEntries[mutation.Key] = mutation.Entry;
                            break;
                        case CacheMutationKind.Remove:
                            nextEntries.Remove(mutation.Key);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(
                                nameof(mutation.Kind),
                                mutation.Kind,
                                "Unknown cache mutation kind.");
                    }
                }

                CacheSnapshot<TKey, TValue, TStamp> next =
                    new CacheSnapshot<TKey, TValue, TStamp>(
                        current.Generation + 1L,
                        nextEntries,
                        keyComparer,
                        takeOwnership: true);
                Volatile.Write(ref snapshot, next);
                return next;
            }
        }
    }
}
