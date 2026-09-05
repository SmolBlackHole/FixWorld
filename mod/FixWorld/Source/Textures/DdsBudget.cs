using System;

namespace FixWorld.Textures
{
    internal static class DdsBudget
    {
        internal const long GiB = 1024L * 1024L * 1024L;

        internal static long EffectiveMaximum(long maximumBytes, long cacheBytes,
            long availableBytes, long minimumFreeBytes)
        {
            if (maximumBytes < 0 || cacheBytes < 0 || availableBytes < 0 || minimumFreeBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (availableBytes < minimumFreeBytes)
                return Math.Min(maximumBytes, Math.Max(0L, cacheBytes - (minimumFreeBytes - availableBytes)));
            long headroom = availableBytes - minimumFreeBytes;
            long permitted = cacheBytes > long.MaxValue - headroom ? long.MaxValue : cacheBytes + headroom;
            return Math.Min(maximumBytes, permitted);
        }
    }
}
