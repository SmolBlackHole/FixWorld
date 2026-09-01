namespace FixWorld.Caching
{
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
}
