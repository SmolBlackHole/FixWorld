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
            GenFilePaths.ContentPath<AssetBundle>(),
            "Assemblies/"
        };

        private readonly SnapshotCache<string, ModFileSet, long> cache =
            new SnapshotCache<string, ModFileSet, long>(
                keyComparer: StringComparer.Ordinal);
        private long generation;

        internal void Rebuild(IReadOnlyList<ModContentPack> mods)
        {
            if (mods == null)
            {
                throw new ArgumentNullException(nameof(mods));
            }

            long nextGeneration = ++generation;
            CacheWriter<string, ModFileSet, long> writer = cache.Writer;
            foreach (KeyValuePair<
                         string,
                         CacheEntry<ModFileSet, long>> entry in
                     writer.SnapshotEntries())
            {
                writer.Remove(entry.Key);
            }

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
            return GetFileSet(
                    mod,
                    contentPath,
                    foldersToLoadDebug)
                .Filter(validateExtension);
        }

        internal List<Tuple<string, FileInfo>> GetFilesPreserveOrder(
            ModContentPack mod,
            string contentPath,
            Func<string, bool> validateExtension,
            IReadOnlyList<string> foldersToLoadDebug = null)
        {
            return GetFileSet(
                    mod,
                    contentPath,
                    foldersToLoadDebug)
                .FilterPreserveOrder(validateExtension);
        }

        private ModFileSet GetFileSet(
            ModContentPack mod,
            string contentPath,
            IReadOnlyList<string> foldersToLoadDebug)
        {
            if (mod == null)
            {
                throw new ArgumentNullException(nameof(mod));
            }

            if (contentPath == null)
            {
                throw new ArgumentNullException(nameof(contentPath));
            }

            if (foldersToLoadDebug != null)
            {
                return Discover(foldersToLoadDebug, contentPath);
            }

            return cache.Snapshot.TryGet(
                GetKey(mod.PackageId, contentPath),
                out CacheEntry<ModFileSet, long> entry)
                ? entry.Value
                : Discover(mod.foldersToLoadDescendingOrder, contentPath);
        }

        private static ModFileSet Discover(
            IReadOnlyList<string> folders,
            string contentPath)
        {
            List<IndexedModFile> files = new List<IndexedModFile>();
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
                    files.Add(new IndexedModFile(key, file, folderIndex));
                }
            }

            return new ModFileSet(files);
        }

        private static string GetKey(string packageId, string contentPath)
        {
            return packageId.ToLowerInvariant() + "\n" +
                   contentPath
                       .Trim('/', '\\')
                       .Replace('\\', '/')
                       .ToLowerInvariant();
        }
    }

    internal sealed class ModFileSet
    {
        private readonly IndexedModFile[] files;

        internal ModFileSet(IEnumerable<IndexedModFile> files)
        {
            this.files = files?.ToArray() ??
                         Array.Empty<IndexedModFile>();
        }

        internal Dictionary<string, FileInfo> Filter(
            Func<string, bool> validateExtension)
        {
            Dictionary<string, FileInfo> result =
                new Dictionary<string, FileInfo>(StringComparer.Ordinal);
            foreach (IndexedModFile file in files)
            {
                if (validateExtension == null ||
                    validateExtension(file.File.Extension))
                {
                    if (!result.ContainsKey(file.Key))
                    {
                        result.Add(file.Key, file.File);
                    }
                }
            }

            return result;
        }

        internal List<Tuple<string, FileInfo>> FilterPreserveOrder(
            Func<string, bool> validateExtension)
        {
            List<Tuple<string, FileInfo>> result =
                new List<Tuple<string, FileInfo>>();
            int maximumFolderIndex = files.Length == 0
                ? -1
                : files.Max(file => file.FolderIndex);
            for (int folderIndex = maximumFolderIndex;
                 folderIndex >= 0;
                 folderIndex--)
            {
                IndexedModFile[] folderFiles = files
                    .Where(file =>
                        file.FolderIndex == folderIndex &&
                        (validateExtension == null ||
                         validateExtension(file.File.Extension)))
                    .ToArray();
                Array.Sort(
                    folderFiles,
                    (left, right) =>
                        left.File.Name.CompareTo(right.File.Name));
                foreach (IndexedModFile file in folderFiles)
                {
                    result.Add(Tuple.Create(file.Key, file.File));
                }
            }

            HashSet<string> seen = new HashSet<string>();
            for (int index = result.Count - 1; index >= 0; index--)
            {
                if (!seen.Add(result[index].Item1))
                {
                    result.RemoveAt(index);
                }
            }

            return result;
        }

    }

    internal readonly struct IndexedModFile
    {
        internal IndexedModFile(string key, FileInfo file, int folderIndex)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            File = file ?? throw new ArgumentNullException(nameof(file));
            FolderIndex = folderIndex;
        }

        internal string Key { get; }

        internal FileInfo File { get; }

        internal int FolderIndex { get; }
    }
}
