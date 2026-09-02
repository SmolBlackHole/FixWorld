using System.Collections.Generic;
using System.Threading;

namespace FixWorld.Caching
{
    public sealed class SnapshotCache<TKey, TValue, TStamp>
    {
        private readonly IEqualityComparer<TKey> keyComparer;
        private CacheSnapshot<TKey, TValue, TStamp> snapshot;

        public SnapshotCache(
            IDictionary<TKey, CacheEntry<TValue, TStamp>> initialEntries = null,
            IEqualityComparer<TKey> keyComparer = null)
        {
            this.keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            snapshot = new CacheSnapshot<TKey, TValue, TStamp>(
                initialEntries,
                this.keyComparer);
            Writer = new CacheWriter<TKey, TValue, TStamp>(
                this,
                initialEntries,
                this.keyComparer);
        }

        public CacheSnapshot<TKey, TValue, TStamp> Snapshot =>
            Volatile.Read(ref snapshot);

        public CacheWriter<TKey, TValue, TStamp> Writer { get; }

        internal CacheSnapshot<TKey, TValue, TStamp> Publish(
            IDictionary<TKey, CacheEntry<TValue, TStamp>> entries)
        {
            CacheSnapshot<TKey, TValue, TStamp> next =
                new CacheSnapshot<TKey, TValue, TStamp>(entries, keyComparer);
            Volatile.Write(ref snapshot, next);
            return next;
        }
    }

    public sealed class CacheWriter<TKey, TValue, TStamp>
    {
        private readonly object sync = new object();
        private readonly SnapshotCache<TKey, TValue, TStamp> cache;
        private readonly Dictionary<TKey, CacheEntry<TValue, TStamp>> entries;
        private bool changed;

        internal CacheWriter(
            SnapshotCache<TKey, TValue, TStamp> cache,
            IDictionary<TKey, CacheEntry<TValue, TStamp>> initialEntries,
            IEqualityComparer<TKey> keyComparer)
        {
            this.cache = cache;
            entries = initialEntries == null
                ? new Dictionary<TKey, CacheEntry<TValue, TStamp>>(keyComparer)
                : new Dictionary<TKey, CacheEntry<TValue, TStamp>>(
                    initialEntries,
                    keyComparer);
        }

        public int Count
        {
            get
            {
                lock (sync)
                {
                    return entries.Count;
                }
            }
        }

        public bool TryGet(
            TKey key,
            out CacheEntry<TValue, TStamp> entry)
        {
            lock (sync)
            {
                return entries.TryGetValue(key, out entry);
            }
        }

        public void Upsert(TKey key, TValue value, TStamp stamp)
        {
            lock (sync)
            {
                entries[key] = new CacheEntry<TValue, TStamp>(value, stamp);
                changed = true;
            }
        }

        public bool Remove(TKey key)
        {
            lock (sync)
            {
                if (!entries.Remove(key))
                {
                    return false;
                }

                changed = true;
                return true;
            }
        }

        public KeyValuePair<TKey, CacheEntry<TValue, TStamp>>[]
            SnapshotEntries()
        {
            lock (sync)
            {
                KeyValuePair<TKey, CacheEntry<TValue, TStamp>>[] snapshot =
                    new KeyValuePair<TKey, CacheEntry<TValue, TStamp>>[
                        entries.Count];
                int index = 0;
                foreach (KeyValuePair<
                             TKey,
                             CacheEntry<TValue, TStamp>> entry in entries)
                {
                    snapshot[index++] = entry;
                }

                return snapshot;
            }
        }

        public CacheSnapshot<TKey, TValue, TStamp> Publish()
        {
            lock (sync)
            {
                if (!changed)
                {
                    return cache.Snapshot;
                }

                CacheSnapshot<TKey, TValue, TStamp> snapshot =
                    cache.Publish(entries);
                changed = false;
                return snapshot;
            }
        }
    }
}
