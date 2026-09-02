using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FixWorld.Caching;
using UnityEngine;
using Verse;

namespace FixWorld.Content
{
    internal sealed class ModFileIndex
    {
        private static readonly string[] IndexedContentPaths =
        {
            GenFilePaths.ContentPath<AudioClip>(),
            GenFilePaths.ContentPath<Texture2D>(),
            GenFilePaths.ContentPath<string>(),
            GenFilePaths.ContentPath<AssetBundle>()
        };

        private readonly SnapshotCache<string, ModFileSet, long> cache =
            new SnapshotCache<string, ModFileSet, long>(
                keyComparer: StringComparer.Ordinal);
        private long generation;

        internal void Clear()
        {
            CacheWriter<string, ModFileSet, long> writer = cache.Writer;
            foreach (KeyValuePair<
                         string,
                         CacheEntry<ModFileSet, long>> entry in
                     writer.SnapshotEntries())
            {
                writer.Remove(entry.Key);
            }

            writer.Publish();
        }

        internal void Rebuild(IReadOnlyList<ModContentPack> mods)
        {
            if (mods == null)
            {
                throw new ArgumentNullException(nameof(mods));
            }

            Clear();
            long nextGeneration = ++generation;
            CacheWriter<string, ModFileSet, long> writer = cache.Writer;
            foreach (ModContentPack mod in mods)
            {
                foreach (string contentPath in IndexedContentPaths)
                {
                    ModFileSet files = Discover(
                        mod.foldersToLoadDescendingOrder,
                        contentPath);
                    writer.Upsert(
                        GetKey(mod.PackageId, contentPath),
                        files,
                        nextGeneration);
                }
            }

            writer.Publish();
        }

        internal Dictionary<string, FileInfo> GetFiles(
            ModContentPack mod,
            string contentPath,
            Func<string, bool> validateExtension,
            IReadOnlyList<string> foldersToLoadDebug = null)
        {
            if (mod == null)
            {
                throw new ArgumentNullException(nameof(mod));
            }

            if (contentPath == null)
            {
                throw new ArgumentNullException(nameof(contentPath));
            }

            ModFileSet files;
            if (foldersToLoadDebug != null)
            {
                files = Discover(foldersToLoadDebug, contentPath);
            }
            else if (!cache.Snapshot.TryGet(
                         GetKey(mod.PackageId, contentPath),
                         out CacheEntry<ModFileSet, long> entry))
            {
                files = Discover(
                    mod.foldersToLoadDescendingOrder,
                    contentPath);
            }
            else
            {
                files = entry.Value;
            }

            return files.Filter(validateExtension);
        }

        private static ModFileSet Discover(
            IReadOnlyList<string> folders,
            string contentPath)
        {
            Dictionary<string, FileInfo> files =
                new Dictionary<string, FileInfo>(StringComparer.Ordinal);
            for (int folderIndex = 0; folderIndex < folders.Count; folderIndex++)
            {
                string folder = folders[folderIndex];
                DirectoryInfo directory = new DirectoryInfo(
                    Path.Combine(folder, contentPath));
                if (!directory.Exists)
                {
                    continue;
                }

                foreach (FileInfo file in directory.GetFiles(
                             "*.*",
                             SearchOption.AllDirectories))
                {
                    string key = file.FullName.Substring(folder.Length + 1);
                    if (!files.ContainsKey(key))
                    {
                        files.Add(key, file);
                    }
                }
            }

            return new ModFileSet(files);
        }

        private static string GetKey(string packageId, string contentPath)
        {
            return packageId.ToLowerInvariant() + "\n" +
                   contentPath.Replace('\\', '/').ToLowerInvariant();
        }
    }

    internal sealed class ModFileSet
    {
        private readonly KeyValuePair<string, FileInfo>[] files;

        internal ModFileSet(IEnumerable<KeyValuePair<string, FileInfo>> files)
        {
            this.files = files?.ToArray() ??
                         Array.Empty<KeyValuePair<string, FileInfo>>();
        }

        internal Dictionary<string, FileInfo> Filter(
            Func<string, bool> validateExtension)
        {
            Dictionary<string, FileInfo> result =
                new Dictionary<string, FileInfo>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, FileInfo> file in files)
            {
                if (validateExtension == null ||
                    validateExtension(file.Value.Extension))
                {
                    result.Add(file.Key, file.Value);
                }
            }

            return result;
        }
    }
}
