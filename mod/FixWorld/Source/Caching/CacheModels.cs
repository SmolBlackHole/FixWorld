using System;

namespace FixWorld.Caching
{
    internal enum CacheLookupState
    {
        Hit,
        Miss,
        Stale,
        Failed
    }

    internal readonly struct CacheEntry<TValue, TStamp>
    {
        internal readonly TValue Value;
        internal readonly TStamp Stamp;

        internal CacheEntry(TValue value, TStamp stamp)
        {
            Value = value;
            Stamp = stamp;
        }
    }

    internal readonly struct CacheLookup<TValue, TStamp>
    {
        internal readonly CacheLookupState State;
        internal readonly CacheEntry<TValue, TStamp> Entry;
        internal readonly Exception Error;

        internal bool HasValue => State == CacheLookupState.Hit;

        private CacheLookup(
            CacheLookupState state,
            CacheEntry<TValue, TStamp> entry,
            Exception error)
        {
            State = state;
            Entry = entry;
            Error = error;
        }

        internal static CacheLookup<TValue, TStamp> Hit(
            CacheEntry<TValue, TStamp> entry)
        {
            return new CacheLookup<TValue, TStamp>(
                CacheLookupState.Hit,
                entry,
                null);
        }

        internal static CacheLookup<TValue, TStamp> Miss()
        {
            return new CacheLookup<TValue, TStamp>(
                CacheLookupState.Miss,
                default,
                null);
        }

        internal static CacheLookup<TValue, TStamp> Stale(
            CacheEntry<TValue, TStamp> entry)
        {
            return new CacheLookup<TValue, TStamp>(
                CacheLookupState.Stale,
                entry,
                null);
        }

        internal static CacheLookup<TValue, TStamp> Failed(Exception error)
        {
            return new CacheLookup<TValue, TStamp>(
                CacheLookupState.Failed,
                default,
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }

    internal enum CacheMutationKind
    {
        Upsert,
        Remove
    }

    internal readonly struct CacheMutation<TKey, TValue, TStamp>
    {
        internal readonly CacheMutationKind Kind;
        internal readonly TKey Key;
        internal readonly CacheEntry<TValue, TStamp> Entry;

        private CacheMutation(
            CacheMutationKind kind,
            TKey key,
            CacheEntry<TValue, TStamp> entry)
        {
            Kind = kind;
            Key = key;
            Entry = entry;
        }

        internal static CacheMutation<TKey, TValue, TStamp> Upsert(
            TKey key,
            TValue value,
            TStamp stamp)
        {
            return new CacheMutation<TKey, TValue, TStamp>(
                CacheMutationKind.Upsert,
                key,
                new CacheEntry<TValue, TStamp>(value, stamp));
        }

        internal static CacheMutation<TKey, TValue, TStamp> Remove(TKey key)
        {
            return new CacheMutation<TKey, TValue, TStamp>(
                CacheMutationKind.Remove,
                key,
                default);
        }
    }
}
