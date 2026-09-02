using System;
using System.Globalization;
using System.IO;
using FixWorld.Migrations;
using FixWorld.Textures;

namespace FixWorld.Tool
{
    internal static class DdsCacheCleanup
    {
        private const string CacheRootEnvironmentVariable =
            "FIXWORLD_DDS_CACHE_ROOT";

        internal static int Run(string[] args)
        {
            string command = args.Length > 0
                ? args[0].ToLowerInvariant()
                : "status";
            bool delete;
            switch (command)
            {
                case "status":
                    delete = false;
                    break;
                case "clean":
                    delete = true;
                    break;
                default:
                    throw new ArgumentException(
                        "DDS cache command must be 'status' or 'clean'.");
            }

            if (args.Length > 2)
            {
                throw new ArgumentException(
                    "DDS cache command accepts at most one cache directory.");
            }

            string root = ResolveRoot(
                args.Length > 1 ? args[1] : DefaultRoot());
            if (delete)
            {
                Program.RequireGameStopped();
            }

            MigrationCleanupResult result = delete
                ? LegacyDdsCacheMigration.Clean(root)
                : LegacyDdsCacheMigration.Inspect(root);
            Print(result, delete);
            if (result.Errors.Count == 0)
            {
                return 0;
            }

            Console.Error.WriteLine(
                "Some cache entries could not be deleted:" +
                Environment.NewLine + string.Join(Environment.NewLine, result.Errors));
            return 1;
        }

        private static void Print(MigrationCleanupResult result, bool delete)
        {
            Console.WriteLine("FixWorld legacy DDS cache: " + result.Root);
            Console.WriteLine("Mode: " + (delete ? "DELETE" : "DRY RUN"));
            Console.WriteLine("Files: " + result.Files);
            Console.WriteLine(
                "Size: " + FormatByteSize(result.Bytes));
            if (!delete)
            {
                Console.WriteLine(
                    "Nothing was deleted. Run 'dds-cache clean' to remove " +
                    "the legacy cache.");
            }
        }

        private static string DefaultRoot()
        {
            string configured = Environment.GetEnvironmentVariable(
                CacheRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured) &&
                string.Equals(
                    Path.GetFileName(Path.GetFullPath(configured).TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)),
                    DdsCacheContract.LegacyCacheDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return configured;
            }

            string userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            return Path.Combine(
                userProfile,
                "AppData",
                "LocalLow",
                "Ludeon Studios",
                "RimWorld by Ludeon Studios",
                "FixWorld",
                "TextureCache",
                DdsCacheContract.LegacyCacheDirectoryName);
        }

        private static string ResolveRoot(string requestedPath)
        {
            string resolved = Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(requestedPath))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            if (!string.Equals(
                    Path.GetFileName(resolved),
                    DdsCacheContract.LegacyCacheDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing cache root that does not end in '" +
                    DdsCacheContract.LegacyCacheDirectoryName + "': " +
                    resolved);
            }

            return resolved;
        }

        private static string FormatByteSize(long bytes)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double value = bytes;
            foreach (string unit in units)
            {
                if (value < 1024.0 || unit == units[units.Length - 1])
                {
                    return value.ToString("N2", CultureInfo.InvariantCulture) +
                           " " + unit;
                }

                value /= 1024.0;
            }

            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }
    }
}
