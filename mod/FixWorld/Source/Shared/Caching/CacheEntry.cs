namespace FixWorld.Caching
{
    public readonly struct CacheEntry<TValue, TStamp>
    {
        public CacheEntry(TValue value, TStamp stamp)
        {
            Value = value;
            Stamp = stamp;
        }

        public TValue Value { get; }

        public TStamp Stamp { get; }
    }
}
