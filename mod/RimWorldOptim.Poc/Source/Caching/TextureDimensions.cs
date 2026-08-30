using System;
using System.IO;

namespace RimWorldOptim.Poc.Caching
{
    internal readonly struct TextureDimensions
    {
        internal readonly int Width;
        internal readonly int Height;

        private TextureDimensions(int width, int height)
        {
            Width = width;
            Height = height;
        }

        internal static bool TryRead(FileInfo file, out TextureDimensions dimensions)
        {
            string extension = file.Extension.ToLowerInvariant();
            if (extension == ".png")
            {
                return TryReadPng(file.FullName, out dimensions);
            }

            if (extension == ".jpg" || extension == ".jpeg")
            {
                return TryReadJpeg(file.FullName, out dimensions);
            }

            dimensions = default;
            return false;
        }

        internal int GetBc3MipCount()
        {
            int minimumDimension = Math.Min(Width, Height);
            if (minimumDimension <= 16 || Width % 4 != 0 || Height % 4 != 0)
            {
                return 0;
            }

            int supportedMipCount = 0;
            int mipWidth = Width;
            int mipHeight = Height;
            while (mipWidth >= 4 && mipHeight >= 4 && mipWidth % 4 == 0 && mipHeight % 4 == 0)
            {
                supportedMipCount++;
                mipWidth >>= 1;
                mipHeight >>= 1;
            }

            int requestedMipCount = (int)Math.Floor(Math.Log(minimumDimension / 16.0, 2.0)) + 1;
            return Math.Min(supportedMipCount, requestedMipCount);
        }

        internal long GetBc3FileSize(int mipCount)
        {
            const int ddsHeaderBytes = 128;
            long bytes = ddsHeaderBytes;
            int mipWidth = Width;
            int mipHeight = Height;
            for (int mip = 0; mip < mipCount; mip++)
            {
                long blocksWide = (mipWidth + 3L) / 4L;
                long blocksHigh = (mipHeight + 3L) / 4L;
                bytes += blocksWide * blocksHigh * 16L;
                mipWidth = Math.Max(1, mipWidth >> 1);
                mipHeight = Math.Max(1, mipHeight >> 1);
            }

            return bytes;
        }

        private static bool TryReadPng(string path, out TextureDimensions dimensions)
        {
            dimensions = default;
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] header = new byte[24];
                if (stream.Read(header, 0, header.Length) != header.Length ||
                    header[0] != 137 ||
                    header[1] != 80 ||
                    header[2] != 78 ||
                    header[3] != 71)
                {
                    return false;
                }

                int width = ReadBigEndianInt32(header, 16);
                int height = ReadBigEndianInt32(header, 20);
                if (width <= 0 || height <= 0)
                {
                    return false;
                }

                dimensions = new TextureDimensions(width, height);
                return true;
            }
        }

        private static bool TryReadJpeg(string path, out TextureDimensions dimensions)
        {
            dimensions = default;
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (stream.Length < 4 || reader.ReadByte() != 0xff || reader.ReadByte() != 0xd8)
                {
                    return false;
                }

                while (stream.Position + 4 <= stream.Length)
                {
                    byte markerStart;
                    do
                    {
                        markerStart = reader.ReadByte();
                    }
                    while (markerStart != 0xff && stream.Position < stream.Length);

                    byte marker;
                    do
                    {
                        marker = reader.ReadByte();
                    }
                    while (marker == 0xff && stream.Position < stream.Length);

                    if (marker == 0xd9 || marker == 0xda)
                    {
                        return false;
                    }

                    if (marker == 0x01 || marker >= 0xd0 && marker <= 0xd7)
                    {
                        continue;
                    }

                    int segmentLength = ReadBigEndianUInt16(reader);
                    if (segmentLength < 2 || stream.Position + segmentLength - 2 > stream.Length)
                    {
                        return false;
                    }

                    if (IsStartOfFrame(marker))
                    {
                        if (segmentLength < 7)
                        {
                            return false;
                        }

                        reader.ReadByte();
                        int height = ReadBigEndianUInt16(reader);
                        int width = ReadBigEndianUInt16(reader);
                        if (width <= 0 || height <= 0)
                        {
                            return false;
                        }

                        dimensions = new TextureDimensions(width, height);
                        return true;
                    }

                    stream.Seek(segmentLength - 2, SeekOrigin.Current);
                }
            }

            return false;
        }

        private static bool IsStartOfFrame(byte marker)
        {
            return marker >= 0xc0 && marker <= 0xc3 ||
                   marker >= 0xc5 && marker <= 0xc7 ||
                   marker >= 0xc9 && marker <= 0xcb ||
                   marker >= 0xcd && marker <= 0xcf;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return bytes[offset] << 24 |
                   bytes[offset + 1] << 16 |
                   bytes[offset + 2] << 8 |
                   bytes[offset + 3];
        }

        private static int ReadBigEndianUInt16(BinaryReader reader)
        {
            return reader.ReadByte() << 8 | reader.ReadByte();
        }
    }
}
