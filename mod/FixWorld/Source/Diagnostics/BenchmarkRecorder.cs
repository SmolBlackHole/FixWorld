using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FixWorld.Textures;
using FixWorld.Loading;
using UnityEngine;
using Verse;

namespace FixWorld.Diagnostics
{
    internal static class BenchmarkRecorder
    {
        private const string OutputEnvironmentVariable = "FIXWORLD_BENCHMARK_OUTPUT";

        private static readonly object CompletionSync = new object();
        private static readonly object TexturePathSync = new object();
        private static readonly Dictionary<string, List<TextureOwner>> OwnersByPath =
            new Dictionary<string, List<TextureOwner>>(StringComparer.Ordinal);

        private static readonly string OutputPath =
            Environment.GetEnvironmentVariable(OutputEnvironmentVariable);

        private static bool reportWritten;
        private static long fileCalls;
        private static long filesFound;
        private static long fileTicks;
        private static long textureFileCalls;
        private static long textureFilesFound;
        private static long textureFileTicks;

        internal static bool Enabled => !string.IsNullOrWhiteSpace(OutputPath);

        internal static long BeginFileDiscovery()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void ObserveFiles(
            long startedAt,
            ModContentPack mod,
            string contentPath,
            Dictionary<string, FileInfo> files)
        {
            if (startedAt == 0L)
            {
                return;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            int count = files?.Count ?? 0;
            Interlocked.Increment(ref fileCalls);
            Interlocked.Add(ref filesFound, count);
            Interlocked.Add(ref fileTicks, elapsedTicks);

            if (files == null ||
                !string.Equals(
                    contentPath,
                    GenFilePaths.ContentPath<Texture2D>(),
                    StringComparison.Ordinal))
            {
                return;
            }

            Interlocked.Increment(ref textureFileCalls);
            Interlocked.Add(ref textureFilesFound, count);
            Interlocked.Add(ref textureFileTicks, elapsedTicks);
            ObserveTexturePaths(mod, contentPath, files);
        }

        internal static void Complete(string source)
        {
            if (!Enabled)
            {
                return;
            }

            lock (CompletionSync)
            {
                if (reportWritten)
                {
                    return;
                }

                reportWritten = true;
            }

            try
            {
                BenchmarkReport report = BenchmarkReport.Create(
                    source,
                    LoadingTelemetry.GetMeasurement(),
                    GetFileDiscoverySnapshot(),
                    XmlLoadingPipeline.GetSnapshot(),
                    GetTexturePathSnapshot(),
                    TextureProbe.GetSnapshot(),
                    TextureDdsCache.GetSnapshot());
                report.Write(OutputPath);
                Log.Message("[FixWorld] Benchmark report written: " + OutputPath);
            }
            catch (Exception exception)
            {
                Log.Error("[FixWorld] Could not write benchmark report: " + exception);
            }
        }

        internal static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void ObserveTexturePaths(
            ModContentPack mod,
            string contentPath,
            Dictionary<string, FileInfo> files)
        {
            HashSet<string> ddsPaths = new HashSet<string>(
                files.Keys
                    .Select(path => path.Replace('\\', '/').ToLowerInvariant())
                    .Where(path => path.EndsWith(".dds", StringComparison.Ordinal)),
                StringComparer.Ordinal);

            lock (TexturePathSync)
            {
                foreach (KeyValuePair<string, FileInfo> item in files)
                {
                    string sourcePath = item.Key.Replace('\\', '/');
                    string lowerPath = sourcePath.ToLowerInvariant();
                    if (!lowerPath.EndsWith(".dds", StringComparison.Ordinal) &&
                        lowerPath.Length > 4 &&
                        ddsPaths.Contains(lowerPath.Substring(0, lowerPath.Length - 4) + ".dds"))
                    {
                        continue;
                    }

                    string assetPath = NormalizeAssetPath(sourcePath, contentPath);
                    if (!OwnersByPath.TryGetValue(assetPath, out List<TextureOwner> owners))
                    {
                        owners = new List<TextureOwner>();
                        OwnersByPath.Add(assetPath, owners);
                    }

                    owners.Add(new TextureOwner(mod.PackageId, mod.loadOrder, item.Value.Length));
                }
            }
        }

        private static FileDiscoverySnapshot GetFileDiscoverySnapshot()
        {
            return new FileDiscoverySnapshot(
                Interlocked.Read(ref fileCalls),
                Interlocked.Read(ref filesFound),
                ToMilliseconds(Interlocked.Read(ref fileTicks)),
                Interlocked.Read(ref textureFileCalls),
                Interlocked.Read(ref textureFilesFound),
                ToMilliseconds(Interlocked.Read(ref textureFileTicks)));
        }

        private static TexturePathSnapshot GetTexturePathSnapshot()
        {
            lock (TexturePathSync)
            {
                int duplicatePathCount = 0;
                int shadowedFileCount = 0;
                long shadowedByteCount = 0L;
                Dictionary<string, int> shadowedByMod =
                    new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (List<TextureOwner> owners in OwnersByPath.Values)
                {
                    if (owners.Count < 2)
                    {
                        continue;
                    }

                    duplicatePathCount++;
                    bool winnerSkipped = false;
                    foreach (TextureOwner owner in owners.OrderByDescending(item => item.LoadOrder))
                    {
                        if (!winnerSkipped)
                        {
                            winnerSkipped = true;
                            continue;
                        }

                        shadowedFileCount++;
                        shadowedByteCount += owner.Bytes;
                        shadowedByMod[owner.PackageId] =
                            shadowedByMod.TryGetValue(owner.PackageId, out int count)
                                ? count + 1
                                : 1;
                    }
                }

                List<ShadowedModSnapshot> topShadowedMods = shadowedByMod
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Key, StringComparer.Ordinal)
                    .Take(5)
                    .Select(item => new ShadowedModSnapshot(item.Key, item.Value))
                    .ToList();
                return new TexturePathSnapshot(
                    OwnersByPath.Count,
                    duplicatePathCount,
                    shadowedFileCount,
                    shadowedByteCount,
                    topShadowedMods);
            }
        }

        private static string NormalizeAssetPath(string sourcePath, string contentPath)
        {
            string normalizedContentPath = contentPath.Replace('\\', '/').TrimEnd('/') + "/";
            string assetPath = sourcePath.StartsWith(
                    normalizedContentPath,
                    StringComparison.Ordinal)
                ? sourcePath.Substring(normalizedContentPath.Length)
                : sourcePath;
            string extension = Path.GetExtension(assetPath);
            return extension.Length == 0
                ? assetPath
                : assetPath.Substring(0, assetPath.Length - extension.Length);
        }

        private sealed class TextureOwner
        {
            internal readonly string PackageId;
            internal readonly int LoadOrder;
            internal readonly long Bytes;

            internal TextureOwner(string packageId, int loadOrder, long bytes)
            {
                PackageId = packageId;
                LoadOrder = loadOrder;
                Bytes = bytes;
            }
        }
    }

    internal readonly struct FileDiscoverySnapshot
    {
        internal readonly long Calls;
        internal readonly long Files;
        internal readonly double TotalMilliseconds;
        internal readonly long TextureCalls;
        internal readonly long TextureFiles;
        internal readonly double TextureMilliseconds;

        internal FileDiscoverySnapshot(
            long calls,
            long files,
            double totalMilliseconds,
            long textureCalls,
            long textureFiles,
            double textureMilliseconds)
        {
            Calls = calls;
            Files = files;
            TotalMilliseconds = totalMilliseconds;
            TextureCalls = textureCalls;
            TextureFiles = textureFiles;
            TextureMilliseconds = textureMilliseconds;
        }
    }

    internal sealed class TexturePathSnapshot
    {
        internal int Unique { get; }
        internal int DuplicatePaths { get; }
        internal int PotentiallyShadowedFiles { get; }
        internal long PotentiallyShadowedBytes { get; }
        internal IReadOnlyList<ShadowedModSnapshot> TopShadowedMods { get; }

        internal TexturePathSnapshot(
            int unique,
            int duplicatePaths,
            int potentiallyShadowedFiles,
            long potentiallyShadowedBytes,
            IReadOnlyList<ShadowedModSnapshot> topShadowedMods)
        {
            Unique = unique;
            DuplicatePaths = duplicatePaths;
            PotentiallyShadowedFiles = potentiallyShadowedFiles;
            PotentiallyShadowedBytes = potentiallyShadowedBytes;
            TopShadowedMods = topShadowedMods;
        }
    }

    internal readonly struct ShadowedModSnapshot
    {
        internal readonly string PackageId;
        internal readonly int Files;

        internal ShadowedModSnapshot(string packageId, int files)
        {
            PackageId = packageId;
            Files = files;
        }
    }
}
