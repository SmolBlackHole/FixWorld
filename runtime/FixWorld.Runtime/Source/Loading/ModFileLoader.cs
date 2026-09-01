using System;
using System.Collections.Generic;
using System.IO;
using Verse;

namespace FixWorld.Loading
{
    internal static class ModFileLoader
    {
        internal static Dictionary<string, FileInfo> Discover(
            ModContentPack mod,
            string contentPath,
            Func<string, bool> validateExtension,
            List<string> foldersToLoadDebug)
        {
            return DiscoverFiles(
                foldersToLoadDebug ?? mod.foldersToLoadDescendingOrder,
                contentPath,
                validateExtension);
        }

        internal static FileInfo[] DiscoverXml(IReadOnlyList<string> folders)
        {
            Dictionary<string, FileInfo> files = new Dictionary<string, FileInfo>();
            for (int folderIndex = 0; folderIndex < folders.Count; folderIndex++)
            {
                string folder = folders[folderIndex];
                DirectoryInfo directory = new DirectoryInfo(Path.Combine(folder, "Defs"));
                if (!directory.Exists)
                {
                    continue;
                }

                foreach (FileInfo file in directory.GetFiles(
                             "*.xml",
                             SearchOption.AllDirectories))
                {
                    if (file.Name.StartsWith("._", StringComparison.Ordinal) ||
                        file.Name.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string key = file.FullName.Substring(folder.Length + 1);
                    if (!files.ContainsKey(key))
                    {
                        files.Add(key, file);
                    }
                }
            }

            return new List<FileInfo>(files.Values).ToArray();
        }

        internal static Dictionary<string, FileInfo> DiscoverTextures(
            IReadOnlyList<string> folders,
            string contentPath)
        {
            return DiscoverFiles(folders, contentPath, IsTextureExtension);
        }

        private static Dictionary<string, FileInfo> DiscoverFiles(
            IReadOnlyList<string> folders,
            string contentPath,
            Func<string, bool> validateExtension)
        {
            Dictionary<string, FileInfo> files = new Dictionary<string, FileInfo>();
            for (int folderIndex = 0; folderIndex < folders.Count; folderIndex++)
            {
                string folder = folders[folderIndex];
                DirectoryInfo directory = new DirectoryInfo(Path.Combine(folder, contentPath));
                if (!directory.Exists)
                {
                    continue;
                }

                foreach (FileInfo file in directory.GetFiles("*.*", SearchOption.AllDirectories))
                {
                    if (validateExtension != null && !validateExtension(file.Extension))
                    {
                        continue;
                    }

                    string key = file.FullName.Substring(folder.Length + 1);
                    if (!files.ContainsKey(key))
                    {
                        files.Add(key, file);
                    }
                }
            }

            return files;
        }

        private static bool IsTextureExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".png" or ".jpg" or ".jpeg" or ".psd" or ".dds" => true,
                _ => false,
            };

        }
    }
}
