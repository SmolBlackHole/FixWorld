using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FixWorld.Migrations
{
    internal sealed class MigrationCleanupResult
    {
        internal MigrationCleanupResult(
            string root,
            int files,
            long bytes,
            bool removed,
            IReadOnlyList<string> errors)
        {
            Root = root;
            Files = files;
            Bytes = bytes;
            Removed = removed;
            Errors = errors;
        }

        internal string Root { get; }
        internal int Files { get; }
        internal long Bytes { get; }
        internal bool Removed { get; }
        internal IReadOnlyList<string> Errors { get; }
    }

    internal static class MigrationCleanup
    {
        internal static MigrationCleanupResult InspectDirectory(
            string path,
            string expectedName)
        {
            DirectoryInfo root = ResolveOwnedDirectory(path, expectedName);
            if (!root.Exists)
            {
                return Result(root.FullName);
            }

            int files = 0;
            long bytes = 0L;
            Stack<DirectoryInfo> pending = new Stack<DirectoryInfo>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                DirectoryInfo current = pending.Pop();
                RefuseReparsePoint(current);
                foreach (FileSystemInfo entry in
                         current.EnumerateFileSystemInfos())
                {
                    RefuseReparsePoint(entry);
                    if (entry is DirectoryInfo directory)
                    {
                        pending.Push(directory);
                    }
                    else if (entry is FileInfo file)
                    {
                        files++;
                        bytes += file.Length;
                    }
                    else
                    {
                        throw new IOException(
                            "Refusing non-regular migration entry: " +
                            entry.FullName);
                    }
                }
            }

            return Result(root.FullName, files, bytes);
        }

        internal static MigrationCleanupResult DeleteDirectory(
            string path,
            string expectedName,
            CancellationToken cancellationToken = default)
        {
            MigrationCleanupResult inspected =
                InspectDirectory(path, expectedName);
            if (!Directory.Exists(inspected.Root))
            {
                return inspected;
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Delete(inspected.Root, recursive: true);
                return Result(
                    inspected.Root,
                    inspected.Files,
                    inspected.Bytes,
                    removed: true);
            }
            catch (Exception exception)
                when (exception is IOException ||
                      exception is UnauthorizedAccessException)
            {
                return Result(
                    inspected.Root,
                    inspected.Files,
                    inspected.Bytes,
                    errors: new[] { exception.Message });
            }
        }

        internal static int DeleteFiles(params string[] paths)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            int deleted = 0;
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                FileInfo file = new FileInfo(Path.GetFullPath(path));
                RefuseReparsePoint(file);
                file.Delete();
                deleted++;
            }

            return deleted;
        }

        private static DirectoryInfo ResolveOwnedDirectory(
            string path,
            string expectedName)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                string.IsNullOrWhiteSpace(expectedName))
            {
                throw new ArgumentException(
                    "A migration directory and its expected name are required.");
            }

            string resolved = Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(path))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            if (!string.Equals(
                    Path.GetFileName(resolved),
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing migration directory with an unexpected name: " +
                    resolved);
            }

            DirectoryInfo directory = new DirectoryInfo(resolved);
            if (directory.Exists)
            {
                RefuseReparsePoint(directory);
            }

            return directory;
        }

        private static MigrationCleanupResult Result(
            string root,
            int files = 0,
            long bytes = 0L,
            bool removed = false,
            IReadOnlyList<string> errors = null)
        {
            return new MigrationCleanupResult(
                root,
                files,
                bytes,
                removed,
                errors ?? Array.Empty<string>());
        }

        private static void RefuseReparsePoint(FileSystemInfo entry)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Refusing reparse point during migration cleanup: " +
                    entry.FullName);
            }
        }
    }
}
