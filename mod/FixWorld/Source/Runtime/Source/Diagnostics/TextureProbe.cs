using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.Diagnostics
{
    internal static class TextureProbe
    {
        private static long fileCount;
        private static long byteCount;
        private static long readTicks;
        private static long loadImageCount;
        private static long loadImageTicks;
        private static long applyCount;
        private static long applyTicks;
        private static long fastCompressCount;
        private static long fastCompressTicks;
        private static long ddsFileCount;
        private static long ddsByteCount;
        private static long ddsLoadTicks;
        private static long totalTicks;

        internal static long BeginLoad()
        {
            return Stopwatch.GetTimestamp();
        }

        internal static void EndLoad(long startedAt)
        {
            Interlocked.Increment(ref fileCount);
            Interlocked.Add(ref totalTicks, Stopwatch.GetTimestamp() - startedAt);
        }

        internal static byte[] ReadAllBytes(VirtualFile file)
        {
            long startedAt = Stopwatch.GetTimestamp();
            byte[] data = file.ReadAllBytes();
            Interlocked.Add(ref readTicks, Stopwatch.GetTimestamp() - startedAt);
            Interlocked.Add(ref byteCount, data.LongLength);
            return data;
        }

        internal static bool LoadImage(Texture2D texture, byte[] data)
        {
            long startedAt = Stopwatch.GetTimestamp();
            bool result = ImageConversion.LoadImage(texture, data);
            Interlocked.Increment(ref loadImageCount);
            Interlocked.Add(ref loadImageTicks, Stopwatch.GetTimestamp() - startedAt);
            return result;
        }

        internal static void Apply(Texture2D texture, bool updateMipmaps, bool makeNoLongerReadable)
        {
            long startedAt = Stopwatch.GetTimestamp();
            texture.Apply(updateMipmaps, makeNoLongerReadable);
            Interlocked.Increment(ref applyCount);
            Interlocked.Add(ref applyTicks, Stopwatch.GetTimestamp() - startedAt);
        }

        internal static Texture2D FastCompressDXT(Texture2D texture, bool deleteOriginal)
        {
            long startedAt = Stopwatch.GetTimestamp();
            Texture2D result = StaticTextureAtlas.FastCompressDXT(texture, deleteOriginal);
            Interlocked.Increment(ref fastCompressCount);
            Interlocked.Add(ref fastCompressTicks, Stopwatch.GetTimestamp() - startedAt);
            return result;
        }

        internal static long BeginDdsLoad(VirtualFile file)
        {
            Interlocked.Add(ref ddsByteCount, file.Length);
            return Stopwatch.GetTimestamp();
        }

        internal static void EndDdsLoad(long startedAt)
        {
            Interlocked.Increment(ref ddsFileCount);
            Interlocked.Add(ref ddsLoadTicks, Stopwatch.GetTimestamp() - startedAt);
        }

        internal static TextureProbeSnapshot GetSnapshot()
        {
            long measuredTotalTicks = Interlocked.Read(ref totalTicks);
            long measuredReadTicks = Interlocked.Read(ref readTicks);
            double totalMilliseconds = ToMilliseconds(measuredTotalTicks);
            double readMilliseconds = ToMilliseconds(measuredReadTicks);
            double processingMilliseconds = Math.Max(0.0, totalMilliseconds - readMilliseconds);
            double loadImageMilliseconds = ToMilliseconds(
                Interlocked.Read(ref loadImageTicks));
            double applyMilliseconds = ToMilliseconds(
                Interlocked.Read(ref applyTicks));
            double fastCompressMilliseconds = ToMilliseconds(
                Interlocked.Read(ref fastCompressTicks));
            double otherMilliseconds = Math.Max(
                0.0,
                processingMilliseconds - loadImageMilliseconds - applyMilliseconds -
                fastCompressMilliseconds);

            return new TextureProbeSnapshot(
                Interlocked.Read(ref fileCount),
                Interlocked.Read(ref byteCount),
                totalMilliseconds,
                readMilliseconds,
                processingMilliseconds,
                Interlocked.Read(ref loadImageCount),
                loadImageMilliseconds,
                Interlocked.Read(ref applyCount),
                applyMilliseconds,
                Interlocked.Read(ref fastCompressCount),
                fastCompressMilliseconds,
                otherMilliseconds,
                Interlocked.Read(ref ddsFileCount),
                Interlocked.Read(ref ddsByteCount),
                ToMilliseconds(Interlocked.Read(ref ddsLoadTicks)));
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }

    [DataContract]
    internal struct TextureProbeSnapshot
    {
        internal TextureProbeSnapshot(
            long files,
            long bytes,
            double totalMilliseconds,
            double readMilliseconds,
            double processingMilliseconds,
            long loadImageCalls,
            double loadImageMilliseconds,
            long applyCalls,
            double applyMilliseconds,
            long fastCompressCalls,
            double fastCompressMilliseconds,
            double otherMilliseconds,
            long ddsFiles,
            long ddsBytes,
            double ddsMilliseconds)
        {
            Files = files;
            Bytes = bytes;
            TotalMilliseconds = totalMilliseconds;
            ReadMilliseconds = readMilliseconds;
            ProcessingMilliseconds = processingMilliseconds;
            LoadImageCalls = loadImageCalls;
            LoadImageMilliseconds = loadImageMilliseconds;
            ApplyCalls = applyCalls;
            ApplyMilliseconds = applyMilliseconds;
            FastCompressCalls = fastCompressCalls;
            FastCompressMilliseconds = fastCompressMilliseconds;
            OtherMilliseconds = otherMilliseconds;
            DdsFiles = ddsFiles;
            DdsBytes = ddsBytes;
            DdsMilliseconds = ddsMilliseconds;
        }

        [DataMember(Name = "files", Order = 1)]
        internal long Files { get; private set; }

        [DataMember(Name = "bytes", Order = 2)]
        internal long Bytes { get; private set; }

        [DataMember(Name = "totalMs", Order = 3)]
        internal double TotalMilliseconds { get; private set; }

        [DataMember(Name = "readMs", Order = 4)]
        internal double ReadMilliseconds { get; private set; }

        [DataMember(Name = "processingMs", Order = 5)]
        internal double ProcessingMilliseconds { get; private set; }

        [DataMember(Name = "loadImageCalls", Order = 6)]
        internal long LoadImageCalls { get; private set; }

        [DataMember(Name = "loadImageMs", Order = 7)]
        internal double LoadImageMilliseconds { get; private set; }

        [DataMember(Name = "applyCalls", Order = 8)]
        internal long ApplyCalls { get; private set; }

        [DataMember(Name = "applyMs", Order = 9)]
        internal double ApplyMilliseconds { get; private set; }

        [DataMember(Name = "fastCompressCalls", Order = 10)]
        internal long FastCompressCalls { get; private set; }

        [DataMember(Name = "fastCompressMs", Order = 11)]
        internal double FastCompressMilliseconds { get; private set; }

        [DataMember(Name = "otherMs", Order = 12)]
        internal double OtherMilliseconds { get; private set; }

        [DataMember(Name = "ddsFiles", Order = 13)]
        internal long DdsFiles { get; private set; }

        [DataMember(Name = "ddsBytes", Order = 14)]
        internal long DdsBytes { get; private set; }

        [DataMember(Name = "ddsMs", Order = 15)]
        internal double DdsMilliseconds { get; private set; }
    }

}
