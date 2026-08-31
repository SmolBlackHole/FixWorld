using System;
using System.Diagnostics;
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
            double totalMilliseconds = BenchmarkRecorder.ToMilliseconds(measuredTotalTicks);
            double readMilliseconds = BenchmarkRecorder.ToMilliseconds(measuredReadTicks);
            double processingMilliseconds = Math.Max(0.0, totalMilliseconds - readMilliseconds);
            double loadImageMilliseconds = BenchmarkRecorder.ToMilliseconds(
                Interlocked.Read(ref loadImageTicks));
            double applyMilliseconds = BenchmarkRecorder.ToMilliseconds(
                Interlocked.Read(ref applyTicks));
            double fastCompressMilliseconds = BenchmarkRecorder.ToMilliseconds(
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
                BenchmarkRecorder.ToMilliseconds(Interlocked.Read(ref ddsLoadTicks)));
        }
    }

    internal readonly struct TextureProbeSnapshot
    {
        internal readonly long Files;
        internal readonly long Bytes;
        internal readonly double TotalMilliseconds;
        internal readonly double ReadMilliseconds;
        internal readonly double ProcessingMilliseconds;
        internal readonly long LoadImageCalls;
        internal readonly double LoadImageMilliseconds;
        internal readonly long ApplyCalls;
        internal readonly double ApplyMilliseconds;
        internal readonly long FastCompressCalls;
        internal readonly double FastCompressMilliseconds;
        internal readonly double OtherMilliseconds;
        internal readonly long DdsFiles;
        internal readonly long DdsBytes;
        internal readonly double DdsMilliseconds;

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
    }

}
