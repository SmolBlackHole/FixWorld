using System;
using System.IO;

namespace FixWorld.Textures
{
    internal static class DdsPayload
    {
        internal const int HeaderBytes = 148;

        internal static int Validate(long fileSize, long offset, long length,
            uint width, uint height, uint mipCount)
        {
            if (fileSize < 0L || offset < 0L || length < HeaderBytes ||
                length > fileSize || offset > fileSize - length)
                throw new InvalidDataException("DDS slice is outside its pack.");

            if (width == 0 || height == 0 || width > 16384 || height > 16384 ||
                width % 4 != 0 || height % 4 != 0)
                throw new InvalidDataException("DDS dimensions are not supported BC7 dimensions.");

            uint maximumMipCount = 1;
            for (uint dimension = Math.Max(width, height); dimension > 1; dimension >>= 1)
                maximumMipCount++;
            if (mipCount == 0 || mipCount > maximumMipCount)
                throw new InvalidDataException("DDS mip count is invalid for its dimensions.");

            long bytes = 0L;
            for (uint mip = 0; mip < mipCount; mip++)
            {
                bytes += ((width + 3L) / 4L) * ((height + 3L) / 4L) * 16L;
                width = Math.Max(1U, width >> 1);
                height = Math.Max(1U, height >> 1);
            }

            if (length != HeaderBytes + bytes || bytes > int.MaxValue)
                throw new InvalidDataException("DDS payload length does not match its BC7 mip chain.");
            return (int)bytes;
        }
    }
}
