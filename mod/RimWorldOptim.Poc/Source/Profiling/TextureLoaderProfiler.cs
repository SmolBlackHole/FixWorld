using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace RimWorldOptim.Poc.Profiling
{
    internal static class TextureLoaderProfiler
    {
        private const string EnabledEnvironmentVariable = "RIMWORLDOPTIM_PROFILE_TEXTURE_LOAD";

        private static readonly bool Enabled = ProfilerRegistry.IsEnabled(EnabledEnvironmentVariable);

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
            return Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void EndLoad(long startedAt)
        {
            if (startedAt == 0L)
            {
                return;
            }

            Interlocked.Increment(ref fileCount);
            Interlocked.Add(ref totalTicks, Stopwatch.GetTimestamp() - startedAt);
        }

        internal static byte[] ReadAllBytes(VirtualFile file)
        {
            if (!Enabled)
            {
                return file.ReadAllBytes();
            }

            long startedAt = Stopwatch.GetTimestamp();
            byte[] data = file.ReadAllBytes();
            Interlocked.Add(ref readTicks, Stopwatch.GetTimestamp() - startedAt);
            Interlocked.Add(ref byteCount, data.LongLength);
            return data;
        }

        internal static bool LoadImage(Texture2D texture, byte[] data)
        {
            if (!Enabled)
            {
                return ImageConversion.LoadImage(texture, data);
            }

            long startedAt = Stopwatch.GetTimestamp();
            bool result = ImageConversion.LoadImage(texture, data);
            Interlocked.Increment(ref loadImageCount);
            Interlocked.Add(ref loadImageTicks, Stopwatch.GetTimestamp() - startedAt);
            return result;
        }

        internal static void Apply(Texture2D texture, bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (!Enabled)
            {
                texture.Apply(updateMipmaps, makeNoLongerReadable);
                return;
            }

            long startedAt = Stopwatch.GetTimestamp();
            texture.Apply(updateMipmaps, makeNoLongerReadable);
            Interlocked.Increment(ref applyCount);
            Interlocked.Add(ref applyTicks, Stopwatch.GetTimestamp() - startedAt);
        }

        internal static Texture2D FastCompressDXT(Texture2D texture, bool deleteOriginal)
        {
            if (!Enabled)
            {
                return StaticTextureAtlas.FastCompressDXT(texture, deleteOriginal);
            }

            long startedAt = Stopwatch.GetTimestamp();
            Texture2D result = StaticTextureAtlas.FastCompressDXT(texture, deleteOriginal);
            Interlocked.Increment(ref fastCompressCount);
            Interlocked.Add(ref fastCompressTicks, Stopwatch.GetTimestamp() - startedAt);
            return result;
        }

        internal static long BeginDdsLoad(VirtualFile file)
        {
            if (!Enabled)
            {
                return 0L;
            }

            Interlocked.Add(ref ddsByteCount, file.Length);
            return Stopwatch.GetTimestamp();
        }

        internal static void EndDdsLoad(long startedAt)
        {
            if (startedAt == 0L)
            {
                return;
            }

            Interlocked.Increment(ref ddsFileCount);
            Interlocked.Add(ref ddsLoadTicks, Stopwatch.GetTimestamp() - startedAt);
        }

        internal static void WriteSummary()
        {
            if (!Enabled)
            {
                return;
            }

            long measuredTotalTicks = Interlocked.Read(ref totalTicks);
            long measuredReadTicks = Interlocked.Read(ref readTicks);
            double totalMilliseconds = ProfilerRegistry.ToMilliseconds(measuredTotalTicks);
            double readMilliseconds = ProfilerRegistry.ToMilliseconds(measuredReadTicks);
            double processingMilliseconds = Math.Max(0.0, totalMilliseconds - readMilliseconds);
            double loadImageMilliseconds = ProfilerRegistry.ToMilliseconds(Interlocked.Read(ref loadImageTicks));
            double applyMilliseconds = ProfilerRegistry.ToMilliseconds(Interlocked.Read(ref applyTicks));
            double fastCompressMilliseconds = ProfilerRegistry.ToMilliseconds(Interlocked.Read(ref fastCompressTicks));
            double otherMilliseconds = Math.Max(
                0.0,
                processingMilliseconds - loadImageMilliseconds - applyMilliseconds - fastCompressMilliseconds);

            Log.Message(string.Format(
                CultureInfo.InvariantCulture,
                "[RimWorldOptim.Poc] Texture loader profile: files={0}; bytes={1}; totalMs={2:0.###}; readMs={3:0.###}; processingMs={4:0.###}",
                Interlocked.Read(ref fileCount),
                Interlocked.Read(ref byteCount),
                totalMilliseconds,
                readMilliseconds,
                processingMilliseconds));
            Log.Message(string.Format(
                CultureInfo.InvariantCulture,
                "[RimWorldOptim.Poc] Texture main-thread profile: loadImageCalls={0}; loadImageMs={1:0.###}; applyCalls={2}; applyMs={3:0.###}; fastCompressCalls={4}; fastCompressMs={5:0.###}; otherMs={6:0.###}",
                Interlocked.Read(ref loadImageCount),
                loadImageMilliseconds,
                Interlocked.Read(ref applyCount),
                applyMilliseconds,
                Interlocked.Read(ref fastCompressCount),
                fastCompressMilliseconds,
                otherMilliseconds));
            Log.Message(string.Format(
                CultureInfo.InvariantCulture,
                "[RimWorldOptim.Poc] DDS loader profile: files={0}; bytes={1}; totalMs={2:0.###}",
                Interlocked.Read(ref ddsFileCount),
                Interlocked.Read(ref ddsByteCount),
                ProfilerRegistry.ToMilliseconds(Interlocked.Read(ref ddsLoadTicks))));
        }
    }

    [HarmonyPatch(typeof(ModDdsLoader), nameof(ModDdsLoader.TryLoadDds))]
    internal static class DdsLoaderProfilePatch
    {
        [HarmonyPrefix]
        private static void Prefix(VirtualFile file, out long __state)
        {
            __state = TextureLoaderProfiler.BeginDdsLoad(file);
        }

        [HarmonyPostfix]
        private static void Postfix(long __state)
        {
            TextureLoaderProfiler.EndDdsLoad(__state);
        }
    }

    [HarmonyPatch]
    internal static class TextureLoaderProfilePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ModContentLoader<Texture2D>),
                "LoadTextureViaImageConversion");
        }

        [HarmonyPrefix]
        private static void Prefix(out long __state)
        {
            __state = TextureLoaderProfiler.BeginLoad();
        }

        [HarmonyPostfix]
        private static void Postfix(long __state)
        {
            TextureLoaderProfiler.EndLoad(__state);
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo readOriginal = AccessTools.Method(typeof(VirtualFile), nameof(VirtualFile.ReadAllBytes));
            MethodInfo readReplacement = AccessTools.Method(
                typeof(TextureLoaderProfiler),
                nameof(TextureLoaderProfiler.ReadAllBytes));
            MethodInfo loadImageOriginal = AccessTools.Method(
                typeof(ImageConversion),
                nameof(ImageConversion.LoadImage),
                new[] { typeof(Texture2D), typeof(byte[]) });
            MethodInfo loadImageReplacement = AccessTools.Method(
                typeof(TextureLoaderProfiler),
                nameof(TextureLoaderProfiler.LoadImage));
            MethodInfo applyOriginal = AccessTools.Method(
                typeof(Texture2D),
                nameof(Texture2D.Apply),
                new[] { typeof(bool), typeof(bool) });
            MethodInfo applyReplacement = AccessTools.Method(
                typeof(TextureLoaderProfiler),
                nameof(TextureLoaderProfiler.Apply));
            MethodInfo fastCompressOriginal = AccessTools.Method(
                typeof(StaticTextureAtlas),
                nameof(StaticTextureAtlas.FastCompressDXT),
                new[] { typeof(Texture2D), typeof(bool) });
            MethodInfo fastCompressReplacement = AccessTools.Method(
                typeof(TextureLoaderProfiler),
                nameof(TextureLoaderProfiler.FastCompressDXT));
            int readReplacements = 0;
            int loadImageReplacements = 0;
            int applyReplacements = 0;
            int fastCompressReplacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(readOriginal))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = readReplacement;
                    readReplacements++;
                }
                else if (instruction.Calls(loadImageOriginal))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = loadImageReplacement;
                    loadImageReplacements++;
                }
                else if (instruction.Calls(applyOriginal))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = applyReplacement;
                    applyReplacements++;
                }
                else if (instruction.Calls(fastCompressOriginal))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = fastCompressReplacement;
                    fastCompressReplacements++;
                }

                yield return instruction;
            }

            if (readReplacements != 1 ||
                loadImageReplacements != 2 ||
                applyReplacements != 3 ||
                fastCompressReplacements != 1)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Unexpected LoadTextureViaImageConversion call shape: read={0}, loadImage={1}, apply={2}, fastCompress={3}.",
                        readReplacements,
                        loadImageReplacements,
                        applyReplacements,
                        fastCompressReplacements));
            }
        }
    }
}
