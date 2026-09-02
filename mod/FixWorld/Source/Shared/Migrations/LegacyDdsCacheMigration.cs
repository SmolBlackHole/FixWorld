using System;
using System.IO;
using System.Threading;
using FixWorld.Textures;

namespace FixWorld.Migrations
{
    internal static class LegacyDdsCacheMigration
    {
        internal static string GetRoot(string currentCacheRoot)
        {
            string current = Path.GetFullPath(currentCacheRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (!string.Equals(
                    Path.GetFileName(current),
                    DdsCacheContract.CacheDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string parent = Path.GetDirectoryName(current);
            return string.IsNullOrEmpty(parent)
                ? null
                : Path.Combine(
                    parent,
                    DdsCacheContract.LegacyCacheDirectoryName);
        }

        internal static MigrationCleanupResult Inspect(string root)
        {
            return MigrationCleanup.InspectDirectory(
                root,
                DdsCacheContract.LegacyCacheDirectoryName);
        }

        internal static MigrationCleanupResult Clean(
            string root,
            CancellationToken cancellationToken = default)
        {
            return MigrationCleanup.DeleteDirectory(
                root,
                DdsCacheContract.LegacyCacheDirectoryName,
                cancellationToken);
        }
    }
}
