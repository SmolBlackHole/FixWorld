using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FixWorld.Tool
{
    internal static class DdsCacheCleanup
    {
        private const string CacheDirectoryName = "dds-v1";
        private const string CacheRootEnvironmentVariable =
            "FIXWORLD_DDS_CACHE_ROOT";

        private static readonly Regex HashDirectory = new(
            "^[0-9a-f]{64}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex StagingDirectory = new(
            "^\\.staging-[0-9]+-[0-9a-f]{32}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex IndexFile = new(
            "^index(?:\\.backup)?\\.json$|" +
            "^index\\.json\\.tmp-[0-9a-f]{32}$|^index\\.lock$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
            CleanupPlan plan = Inspect(root);
            Print(plan, delete);
            if (!delete || plan.Files.Count == 0)
            {
                if (delete)
                {
                    Console.WriteLine(
                        "No FixWorld DDS cache entries were found.");
                }
                else
                {
                    Console.WriteLine(
                        "Nothing was deleted. Run 'dds-cache clean' to " +
                        "remove the listed entries.");
                }

                return 0;
            }

            Program.RequireGameStopped();
            IReadOnlyList<string> errors = Delete(plan);
            if (errors.Count == 0)
            {
                Console.WriteLine("FixWorld DDS cache cleanup complete.");
                return 0;
            }

            Console.Error.WriteLine(
                "Some cache entries could not be deleted:" +
                Environment.NewLine + string.Join(Environment.NewLine, errors));
            return 1;
        }

        private static CleanupPlan Inspect(string root)
        {
            CleanupPlan plan = new CleanupPlan(root);
            if (!Directory.Exists(root))
            {
                return plan;
            }

            DirectoryInfo rootDirectory = new DirectoryInfo(root);
            RefuseReparsePoint(rootDirectory);
            foreach (FileSystemInfo packageEntry in
                     rootDirectory.EnumerateFileSystemInfos())
            {
                EnsureWithin(root, packageEntry.FullName);
                RefuseReparsePoint(packageEntry);
                if (packageEntry is FileInfo rootFile)
                {
                    if (IndexFile.IsMatch(rootFile.Name))
                    {
                        plan.Add(rootFile, CleanupFileKind.Index);
                    }
                    else
                    {
                        plan.UnknownFiles++;
                    }

                    continue;
                }

                if (!(packageEntry is DirectoryInfo packageDirectory))
                {
                    throw new IOException(
                        "Refusing non-regular cache entry: " +
                        packageEntry.FullName);
                }

                if (StagingDirectory.IsMatch(packageDirectory.Name))
                {
                    CollectStagingTree(plan, packageDirectory);
                    continue;
                }

                plan.Directories.Add(packageDirectory.FullName);
                InspectPackage(plan, packageDirectory);
            }

            return plan;
        }

        private static void InspectPackage(
            CleanupPlan plan,
            DirectoryInfo packageDirectory)
        {
            foreach (FileSystemInfo hashEntry in
                     packageDirectory.EnumerateFileSystemInfos())
            {
                EnsureWithin(plan.Root, hashEntry.FullName);
                RefuseReparsePoint(hashEntry);
                if (hashEntry is FileInfo)
                {
                    plan.UnknownFiles++;
                    continue;
                }

                if (!(hashEntry is DirectoryInfo hashDirectory))
                {
                    throw new IOException(
                        "Refusing non-regular cache entry: " +
                        hashEntry.FullName);
                }

                if (!HashDirectory.IsMatch(hashDirectory.Name))
                {
                    CountUnknownTree(plan, hashDirectory);
                    continue;
                }

                plan.Directories.Add(hashDirectory.FullName);
                foreach (FileSystemInfo cacheEntry in
                         hashDirectory.EnumerateFileSystemInfos())
                {
                    EnsureWithin(plan.Root, cacheEntry.FullName);
                    RefuseReparsePoint(cacheEntry);
                    if (cacheEntry is DirectoryInfo)
                    {
                        throw new IOException(
                            "Refusing unexpected nested cache directory: " +
                            cacheEntry.FullName);
                    }

                    if (!(cacheEntry is FileInfo cacheFile))
                    {
                        throw new IOException(
                            "Refusing non-regular cache entry: " +
                            cacheEntry.FullName);
                    }

                    if (string.Equals(
                            cacheFile.Extension,
                            ".dds",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        plan.Add(cacheFile, CleanupFileKind.Dds);
                    }
                    else
                    {
                        plan.UnknownFiles++;
                    }
                }
            }
        }

        private static void CollectStagingTree(
            CleanupPlan plan,
            DirectoryInfo directory)
        {
            Stack<DirectoryInfo> pending = new Stack<DirectoryInfo>();
            pending.Push(directory);
            while (pending.Count > 0)
            {
                DirectoryInfo current = pending.Pop();
                EnsureWithin(plan.Root, current.FullName);
                RefuseReparsePoint(current);
                plan.Directories.Add(current.FullName);
                foreach (FileSystemInfo entry in
                         current.EnumerateFileSystemInfos())
                {
                    EnsureWithin(plan.Root, entry.FullName);
                    RefuseReparsePoint(entry);
                    if (entry is DirectoryInfo childDirectory)
                    {
                        pending.Push(childDirectory);
                    }
                    else if (entry is FileInfo file)
                    {
                        plan.Add(file, CleanupFileKind.Staging);
                    }
                    else
                    {
                        throw new IOException(
                            "Refusing non-regular cache entry: " +
                            entry.FullName);
                    }
                }
            }
        }

        private static void CountUnknownTree(
            CleanupPlan plan,
            DirectoryInfo directory)
        {
            Stack<DirectoryInfo> pending = new Stack<DirectoryInfo>();
            pending.Push(directory);
            while (pending.Count > 0)
            {
                DirectoryInfo current = pending.Pop();
                EnsureWithin(plan.Root, current.FullName);
                RefuseReparsePoint(current);
                foreach (FileSystemInfo entry in
                         current.EnumerateFileSystemInfos())
                {
                    EnsureWithin(plan.Root, entry.FullName);
                    RefuseReparsePoint(entry);
                    if (entry is DirectoryInfo childDirectory)
                    {
                        pending.Push(childDirectory);
                    }
                    else if (entry is FileInfo)
                    {
                        plan.UnknownFiles++;
                    }
                    else
                    {
                        throw new IOException(
                            "Refusing non-regular cache entry: " +
                            entry.FullName);
                    }
                }
            }
        }

        private static IReadOnlyList<string> Delete(CleanupPlan plan)
        {
            List<string> errors = [];
            int deletedFiles = 0;
            foreach (CleanupFile file in plan.Files)
            {
                try
                {
                    EnsureWithin(plan.Root, file.Path);
                    if (!File.Exists(file.Path))
                    {
                        continue;
                    }

                    FileInfo current = new FileInfo(file.Path);
                    RefuseReparsePoint(current);
                    current.Delete();
                    deletedFiles++;
                }
                catch (Exception exception)
                {
                    errors.Add(file.Path + ": " + exception.Message);
                }
            }

            foreach (string directoryPath in plan.Directories
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(path => path.Length))
            {
                try
                {
                    EnsureWithin(plan.Root, directoryPath);
                    if (!Directory.Exists(directoryPath))
                    {
                        continue;
                    }

                    DirectoryInfo current = new DirectoryInfo(directoryPath);
                    RefuseReparsePoint(current);
                    if (!current.EnumerateFileSystemInfos().Any())
                    {
                        current.Delete();
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(directoryPath + ": " + exception.Message);
                }
            }

            Console.WriteLine("Deleted files: " + deletedFiles);
            return errors;
        }

        private static void Print(CleanupPlan plan, bool delete)
        {
            Console.WriteLine("FixWorld DDS cache: " + plan.Root);
            Console.WriteLine("Mode: " + (delete ? "DELETE" : "DRY RUN"));
            Console.WriteLine("DDS files: " + plan.DdsFiles);
            Console.WriteLine("Staging files: " + plan.StagingFiles);
            Console.WriteLine("Index files: " + plan.IndexFiles);
            Console.WriteLine(
                "Removable size: " + FormatByteSize(plan.RemovableBytes));
            Console.WriteLine(
                "Unknown files left untouched: " + plan.UnknownFiles);
        }

        private static string DefaultRoot()
        {
            string configured = Environment.GetEnvironmentVariable(
                CacheRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
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
                CacheDirectoryName);
        }

        private static string ResolveRoot(string requestedPath)
        {
            string expanded = Environment.ExpandEnvironmentVariables(
                requestedPath);
            string resolved = Path.GetFullPath(expanded).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (!string.Equals(
                    Path.GetFileName(resolved),
                    CacheDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing cache root that does not end in '" +
                    CacheDirectoryName + "': " + resolved);
            }

            string driveRoot = Path.GetPathRoot(resolved).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string userProfile = Path.GetFullPath(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            if (string.Equals(
                    resolved,
                    driveRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    resolved,
                    userProfile,
                    StringComparison.OrdinalIgnoreCase) ||
                userProfile.StartsWith(
                    resolved + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing broad cache root: " + resolved);
            }

            string relative = resolved.Substring(
                Path.GetPathRoot(resolved).Length);
            int segmentCount = relative.Split(
                [
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ],
                StringSplitOptions.RemoveEmptyEntries).Length;
            if (segmentCount < 3)
            {
                throw new InvalidOperationException(
                    "Refusing unusually broad cache root: " + resolved);
            }

            return resolved;
        }

        private static void EnsureWithin(string root, string candidate)
        {
            string prefix = root.TrimEnd(
                                Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(candidate);
            if (!resolved.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Path escapes the FixWorld cache: " + resolved);
            }
        }

        private static void RefuseReparsePoint(FileSystemInfo entry)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Refusing reparse point inside cache: " + entry.FullName);
            }
        }

        private static string FormatByteSize(long bytes)
        {
            string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
            double value = bytes;
            foreach (string unit in units)
            {
                if (value < 1024.0 || unit == units[units.Length - 1])
                {
                    return value.ToString("N2") + " " + unit;
                }

                value /= 1024.0;
            }

            return bytes + " B";
        }

        private enum CleanupFileKind
        {
            Dds,
            Staging,
            Index
        }

        private sealed class CleanupPlan
        {
            internal CleanupPlan(string root)
            {
                Root = root;
            }

            internal string Root { get; }

            internal List<CleanupFile> Files { get; } = [];

            internal List<string> Directories { get; } = [];

            internal int DdsFiles { get; private set; }

            internal int StagingFiles { get; private set; }

            internal int IndexFiles { get; private set; }

            internal int UnknownFiles { get; set; }

            internal long RemovableBytes { get; private set; }

            internal void Add(FileInfo file, CleanupFileKind kind)
            {
                Files.Add(new CleanupFile(file.FullName));
                RemovableBytes += file.Length;
                switch (kind)
                {
                    case CleanupFileKind.Dds:
                        DdsFiles++;
                        break;
                    case CleanupFileKind.Staging:
                        StagingFiles++;
                        break;
                    case CleanupFileKind.Index:
                        IndexFiles++;
                        break;
                }
            }
        }

        private readonly struct CleanupFile
        {
            internal CleanupFile(string path)
            {
                Path = path;
            }

            internal string Path { get; }
        }
    }
}
