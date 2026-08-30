using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Verse;

namespace FixWorld.Profiling
{
    internal static class TexturePathProfiler
    {
        private const string EnabledEnvironmentVariable = "FIXWORLD_PROFILE_TEXTURE_PATHS";

        private static readonly bool Enabled = ProfilerRegistry.IsEnabled(EnabledEnvironmentVariable);

        private static readonly Dictionary<string, List<Owner>> OwnersByPath =
            new Dictionary<string, List<Owner>>(StringComparer.Ordinal);

        internal static void Observe(
            ModContentPack mod,
            string contentPath,
            Dictionary<string, FileInfo> files)
        {
            if (!Enabled ||
                files == null ||
                !string.Equals(contentPath, GenFilePaths.ContentPath<UnityEngine.Texture2D>(), StringComparison.Ordinal))
            {
                return;
            }

            HashSet<string> ddsPaths = new HashSet<string>(
                files.Keys
                    .Select(path => path.Replace('\\', '/').ToLowerInvariant())
                    .Where(path => path.EndsWith(".dds", StringComparison.Ordinal)),
                StringComparer.Ordinal);

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
                if (!OwnersByPath.TryGetValue(assetPath, out List<Owner> owners))
                {
                    owners = new List<Owner>();
                    OwnersByPath.Add(assetPath, owners);
                }

                owners.Add(new Owner(mod.PackageId, mod.loadOrder, item.Value.Length));
            }
        }

        internal static void WriteSummary()
        {
            if (!Enabled)
            {
                return;
            }

            int duplicatePathCount = 0;
            int shadowedFileCount = 0;
            long shadowedByteCount = 0L;
            Dictionary<string, int> shadowedByMod = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (List<Owner> owners in OwnersByPath.Values)
            {
                if (owners.Count < 2)
                {
                    continue;
                }

                duplicatePathCount++;
                Owner winner = owners.OrderByDescending(owner => owner.LoadOrder).First();
                bool skippedWinner = false;
                foreach (Owner owner in owners.OrderByDescending(owner => owner.LoadOrder))
                {
                    if (!skippedWinner && ReferenceEquals(owner, winner))
                    {
                        skippedWinner = true;
                        continue;
                    }

                    shadowedFileCount++;
                    shadowedByteCount += owner.Bytes;
                    shadowedByMod[owner.PackageId] = shadowedByMod.TryGetValue(owner.PackageId, out int count)
                        ? count + 1
                        : 1;
                }
            }

            string topShadowedMods = string.Join(
                "|",
                shadowedByMod
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Key, StringComparer.Ordinal)
                    .Take(5)
                    .Select(item => item.Key + "=" + item.Value.ToString(CultureInfo.InvariantCulture)));

            Log.Message(string.Format(
                CultureInfo.InvariantCulture,
                "[FixWorld] Texture path profile: unique={0}; duplicatePaths={1}; potentiallyShadowedFiles={2}; potentiallyShadowedBytes={3}; topShadowedMods={4}",
                OwnersByPath.Count,
                duplicatePathCount,
                shadowedFileCount,
                shadowedByteCount,
                topShadowedMods));
        }

        private static string NormalizeAssetPath(string sourcePath, string contentPath)
        {
            string normalizedContentPath = contentPath.Replace('\\', '/').TrimEnd('/') + "/";
            string assetPath = sourcePath.StartsWith(normalizedContentPath, StringComparison.Ordinal)
                ? sourcePath.Substring(normalizedContentPath.Length)
                : sourcePath;
            string extension = Path.GetExtension(assetPath);
            return extension.Length == 0
                ? assetPath
                : assetPath.Substring(0, assetPath.Length - extension.Length);
        }

        private sealed class Owner
        {
            internal readonly string PackageId;
            internal readonly int LoadOrder;
            internal readonly long Bytes;

            internal Owner(string packageId, int loadOrder, long bytes)
            {
                PackageId = packageId;
                LoadOrder = loadOrder;
                Bytes = bytes;
            }
        }
    }
}
