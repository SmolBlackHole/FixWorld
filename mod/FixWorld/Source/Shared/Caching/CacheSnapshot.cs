using System.Collections;
using System.Collections.Generic;

namespace FixWorld.Caching
{
    public sealed class CacheSnapshot<TKey, TValue, TStamp> :
        IEnumerable<KeyValuePair<TKey, CacheEntry<TValue, TStamp>>>
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

        public int Count => entries.Count;

        public bool TryGet(
            TKey key,
            out CacheEntry<TValue, TStamp> entry)
        {
            return entries.TryGetValue(key, out entry);
        }

        public IEnumerator<KeyValuePair<TKey, CacheEntry<TValue, TStamp>>>
            GetEnumerator()
        {
            return entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
