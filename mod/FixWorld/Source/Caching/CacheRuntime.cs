using System.Collections.Generic;
using System.Threading;

namespace FixWorld.Caching
{
    internal sealed class CacheSnapshot<TKey, TValue, TStamp>
    {
        private readonly Dictionary<TKey, CacheEntry<TValue, TStamp>> entries;

        internal CacheSnapshot(
            IDictionary<TKey, CacheEntry<TValue, TStamp>> entries,
            IEqualityComparer<TKey> keyComparer)
        {
            this.entries = entries == null
                ? new Dictionary<TKey, CacheEntry<TValue, TStamp>>(keyComparer)
                : new Dictionary<TKey, CacheEntry<TValue, TStamp>>(
                    entries,
                    keyComparer);
        }

        internal int Count => entries.Count;

        internal bool TryGet(
            TKey key,
            out CacheEntry<TValue, TStamp> entry)
        {
            return entries.TryGetValue(key, out entry);
        }

        internal IEnumerable<KeyValuePair<TKey, CacheEntry<TValue, TStamp>>>
            Enumerate()
        {
            foreach (KeyValuePair<TKey, CacheEntry<TValue, TStamp>> pair in entries)
            {
                yield return pair;
            }
        }

    }

    internal sealed class CacheWriter<TKey, TValue, TStamp>
    {
        private readonly CacheRuntime<TKey, TValue, TStamp> runtime;
        private readonly Dictionary<TKey, CacheEntry<TValue, TStamp>> entries;
        private bool changed;

        internal CacheWriter(
            CacheRuntime<TKey, TValue, TStamp> runtime,
            IDictionary<TKey, CacheEntry<TValue, TStamp>> initialEntries,
            IEqualityComparer<TKey> keyComparer)
        {
            this.runtime = runtime;
            entries = initialEntries == null
                ? new Dictionary<TKey, CacheEntry<TValue, TStamp>>(keyComparer)
                : new Dictionary<TKey, CacheEntry<TValue, TStamp>>(
                    initialEntries,
                    keyComparer);
        }

        internal int Count => entries.Count;

        internal bool TryGet(TKey key, out CacheEntry<TValue, TStamp> entry)
        {
            return entries.TryGetValue(key, out entry);
        }

        internal void Upsert(TKey key, TValue value, TStamp stamp)
        {
            entries[key] = new CacheEntry<TValue, TStamp>(value, stamp);
            changed = true;
        }

        internal bool Remove(TKey key)
        {
            if (!entries.Remove(key))
            {
                return false;
            }

            changed = true;
            return true;
        }

        internal IEnumerable<KeyValuePair<TKey, CacheEntry<TValue, TStamp>>>
            Enumerate()
        {
            return entries;
        }

        internal CacheSnapshot<TKey, TValue, TStamp> Publish()
        {
            if (!changed)
            {
                return runtime.Snapshot;
            }

            CacheSnapshot<TKey, TValue, TStamp> snapshot =
                runtime.Publish(entries);
            changed = false;
            return snapshot;
        }
    }

    internal sealed class CacheRuntime<TKey, TValue, TStamp>
    {
        private readonly IEqualityComparer<TKey> keyComparer;
        private CacheSnapshot<TKey, TValue, TStamp> snapshot;

        internal CacheRuntime(
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

        internal CacheSnapshot<TKey, TValue, TStamp> Snapshot =>
            Volatile.Read(ref snapshot);

        internal CacheWriter<TKey, TValue, TStamp> Writer { get; }

        internal CacheSnapshot<TKey, TValue, TStamp> Publish(
            IDictionary<TKey, CacheEntry<TValue, TStamp>> entries)
        {
            CacheSnapshot<TKey, TValue, TStamp> next =
                new CacheSnapshot<TKey, TValue, TStamp>(entries, keyComparer);
            Volatile.Write(ref snapshot, next);
            return next;
        }
    }
}
