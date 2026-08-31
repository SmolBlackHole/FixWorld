using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FixWorld.Caching;
using FixWorld.Loading;
using UnityEngine;
using Verse;

namespace FixWorld.Diagnostics
{
    internal static class BenchmarkRecorder
    {
        private const string OutputEnvironmentVariable = "FIXWORLD_BENCHMARK_OUTPUT";

        private static readonly object CompletionSync = new object();
        private static readonly object DelayedActionSync = new object();
        private static readonly object StaticConstructorSync = new object();
        private static readonly object TexturePathSync = new object();
        private static readonly Dictionary<string, DelayedActionStats> DelayedActions =
            new Dictionary<string, DelayedActionStats>(StringComparer.Ordinal);
        private static readonly Dictionary<string, StaticConstructorStats> StaticConstructors =
            new Dictionary<string, StaticConstructorStats>(StringComparer.Ordinal);
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
        private static long staticConstructorTailTicks;

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

        internal static void ObserveDelayedAction(
            Action action,
            string method,
            long elapsedTicks)
        {
            if (!Enabled)
            {
                return;
            }

            DelayedActionOwner owner = FindDelayedActionOwner(action);
            string key = owner.PackageId + "\n" + method;
            lock (DelayedActionSync)
            {
                if (!DelayedActions.TryGetValue(key, out DelayedActionStats stats))
                {
                    stats = new DelayedActionStats(method, owner);
                    DelayedActions.Add(key, stats);
                }

                stats.Calls++;
                stats.TotalTicks += elapsedTicks;
                stats.MaxTicks = Math.Max(stats.MaxTicks, elapsedTicks);
            }
        }

        internal static void ObserveStaticConstructor(
            StaticConstructorTarget target,
            long elapsedTicks,
            bool succeeded)
        {
            if (!Enabled)
            {
                return;
            }

            string typeName = target.Type.FullName ?? target.Type.Name;
            string key = target.PackageId + "\n" + typeName;
            lock (StaticConstructorSync)
            {
                if (!StaticConstructors.TryGetValue(
                        key,
                        out StaticConstructorStats stats))
                {
                    stats = new StaticConstructorStats(typeName, target);
                    StaticConstructors.Add(key, stats);
                }

                stats.Calls++;
                stats.TotalTicks += elapsedTicks;
                stats.MaxTicks = Math.Max(stats.MaxTicks, elapsedTicks);
                if (!succeeded)
                {
                    stats.Failures++;
                }
            }
        }

        internal static void ObserveStaticConstructorTail(long elapsedTicks)
        {
            if (Enabled)
            {
                Interlocked.Add(ref staticConstructorTailTicks, elapsedTicks);
            }
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
                    LoadingSession.GetMeasurement(),
                    GetDelayedActionSnapshot(),
                    GetStaticConstructorSnapshot(),
                    ToMilliseconds(Interlocked.Read(ref staticConstructorTailTicks)),
                    GetFileDiscoverySnapshot(),
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

        private static IReadOnlyList<DelayedActionSnapshot> GetDelayedActionSnapshot()
        {
            lock (DelayedActionSync)
            {
                return DelayedActions.Values
                    .OrderByDescending(item => item.TotalTicks)
                    .Select(item => new DelayedActionSnapshot(
                        item.Method,
                        item.Owner.PackageId,
                        item.Owner.ModName,
                        item.Calls,
                        ToMilliseconds(item.TotalTicks),
                        ToMilliseconds(item.MaxTicks)))
                    .ToList();
            }
        }

        private static IReadOnlyList<StaticConstructorSnapshot>
            GetStaticConstructorSnapshot()
        {
            lock (StaticConstructorSync)
            {
                return StaticConstructors.Values
                    .OrderByDescending(item => item.TotalTicks)
                    .Select(item => new StaticConstructorSnapshot(
                        item.TypeName,
                        item.Target.PackageId,
                        item.Target.ModName,
                        item.Calls,
                        ToMilliseconds(item.TotalTicks),
                        ToMilliseconds(item.MaxTicks),
                        item.Failures))
                    .ToList();
            }
        }

        private static DelayedActionOwner FindDelayedActionOwner(Action action)
        {
            ModContentPack targetMod = FindTargetMod(action.Target);
            Assembly assembly = action.Method.DeclaringType?.Assembly ??
                                action.Method.Module.Assembly;
            ModContentPack assemblyMod = targetMod ??
                                         LoadedModManager.RunningModsListForReading
                                             .FirstOrDefault(mod =>
                                                 mod.assemblies.loadedAssemblies.Contains(assembly));
            if (assemblyMod != null)
            {
                return new DelayedActionOwner(assemblyMod.PackageId, assemblyMod.Name);
            }

            if (assembly == typeof(LongEventHandler).Assembly)
            {
                return new DelayedActionOwner(ModContentPack.CoreModPackageId, "RimWorld");
            }

            string assemblyName = assembly.GetName().Name ?? "unknown";
            return new DelayedActionOwner(assemblyName, assemblyName);
        }

        private static ModContentPack FindTargetMod(object target)
        {
            if (target is ModContentPack directMod)
            {
                return directMod;
            }

            if (target == null)
            {
                return null;
            }

            foreach (FieldInfo field in target.GetType().GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (typeof(ModContentPack).IsAssignableFrom(field.FieldType))
                {
                    return field.GetValue(target) as ModContentPack;
                }
            }

            return null;
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

        private sealed class DelayedActionStats
        {
            internal readonly string Method;
            internal readonly DelayedActionOwner Owner;
            internal long Calls;
            internal long TotalTicks;
            internal long MaxTicks;

            internal DelayedActionStats(string method, DelayedActionOwner owner)
            {
                Method = method;
                Owner = owner;
            }
        }

        private sealed class StaticConstructorStats
        {
            internal readonly string TypeName;
            internal readonly StaticConstructorTarget Target;
            internal long Calls;
            internal long TotalTicks;
            internal long MaxTicks;
            internal long Failures;

            internal StaticConstructorStats(
                string typeName,
                StaticConstructorTarget target)
            {
                TypeName = typeName;
                Target = target;
            }
        }

        private readonly struct DelayedActionOwner
        {
            internal readonly string PackageId;
            internal readonly string ModName;

            internal DelayedActionOwner(string packageId, string modName)
            {
                PackageId = packageId;
                ModName = modName;
            }
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

    internal readonly struct DelayedActionSnapshot
    {
        internal readonly string Method;
        internal readonly string PackageId;
        internal readonly string ModName;
        internal readonly long Calls;
        internal readonly double TotalMilliseconds;
        internal readonly double MaxMilliseconds;

        internal DelayedActionSnapshot(
            string method,
            string packageId,
            string modName,
            long calls,
            double totalMilliseconds,
            double maxMilliseconds)
        {
            Method = method;
            PackageId = packageId;
            ModName = modName;
            Calls = calls;
            TotalMilliseconds = totalMilliseconds;
            MaxMilliseconds = maxMilliseconds;
        }
    }

    internal readonly struct StaticConstructorSnapshot
    {
        internal readonly string TypeName;
        internal readonly string PackageId;
        internal readonly string ModName;
        internal readonly long Calls;
        internal readonly double TotalMilliseconds;
        internal readonly double MaxMilliseconds;
        internal readonly long Failures;

        internal StaticConstructorSnapshot(
            string typeName,
            string packageId,
            string modName,
            long calls,
            double totalMilliseconds,
            double maxMilliseconds,
            long failures)
        {
            TypeName = typeName;
            PackageId = packageId;
            ModName = modName;
            Calls = calls;
            TotalMilliseconds = totalMilliseconds;
            MaxMilliseconds = maxMilliseconds;
            Failures = failures;
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
