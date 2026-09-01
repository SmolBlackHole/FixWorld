using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FixWorld.Textures
{
    internal static class TextureCacheIdentity
    {
        internal static string Normalize(string value)
        {
            return value.Replace('\\', '/').ToLowerInvariant();
        }

        internal static string GetEntryKey(string packageId, string sourcePath)
        {
            return Normalize(packageId) + "\n" + Normalize(sourcePath);
        }

        internal static string GetRelativeSourcePath(FileInfo source, string modRoot)
        {
            return Normalize(source.FullName.Substring(modRoot.Length));
        }

        internal static string GetContentKey(
            string sourcePath,
            string sourceHash,
            string converterIdentity)
        {
            return HashText(
                DdsCacheContract.CacheIdentityVersion + "\n" + sourcePath + "\n" +
                sourceHash + "\n" +
                (converterIdentity ?? "unavailable"));
        }

        internal static string GetFileHash(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        internal static string GetConverterIdentity(string path)
        {
            return "sha256:" + GetFileHash(path);
        }

        internal static string GetDeferredBuildIdentity(
            IReadOnlyList<TextureCacheEntry> entries)
        {
            StringBuilder input = new StringBuilder(entries.Count * 96);
            foreach (TextureCacheEntry entry in entries)
            {
                input.Append(entry.Key)
                    .Append('|')
                    .Append(entry.SourceHash)
                    .Append('\n');
            }

            return HashText(input.ToString());
        }

        internal static string SanitizePathSegment(string value)
        {
            HashSet<char> invalidCharacters = new HashSet<char>(
                Path.GetInvalidFileNameChars());
            return new string(Normalize(value)
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray());
        }

        internal static void EnsureChildPath(string parent, string child)
        {
            string resolvedParent = Path.GetFullPath(parent)
                                        .TrimEnd(Path.DirectorySeparatorChar) +
                                    Path.DirectorySeparatorChar;
            string resolvedChild = Path.GetFullPath(child);
            if (!resolvedChild.StartsWith(
                    resolvedParent,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Invalid cache path: " + resolvedChild);
            }
        }

        private static string HashText(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string ToHex(byte[] hash)
        {
            return BitConverter.ToString(hash)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }
    }
}
