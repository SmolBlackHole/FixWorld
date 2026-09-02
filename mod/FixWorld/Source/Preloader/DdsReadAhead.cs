using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Xml;
using FixWorld.Textures;

namespace FixWorld.Preloader
{
    internal static class DdsReadAhead
    {
        private const long MiB = 1024L * 1024L;
        private const long DefaultMaximumBudgetBytes = 256L * MiB;
        private const int MaximumIndexBytes = 64 * 1024 * 1024;
        private const int ReadBufferBytes = 256 * 1024;

        internal static void Start()
        {
            Thread thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "FixWorld DDS read-ahead",
                Priority = ThreadPriority.BelowNormal
            };
            thread.Start();
        }

        private static void Run()
        {
            long startedAt = Stopwatch.GetTimestamp();
            long budgetBytes = 0L;
            long bytesRead = 0L;
            int filesRead = 0;
            try
            {
                if (string.Equals(
                        Environment.GetEnvironmentVariable(
                            DdsCacheContract.EnabledEnvironmentVariable),
                        "0",
                        StringComparison.Ordinal))
                {
                    Publish("disabled", budgetBytes, bytesRead, filesRead, startedAt);
                    return;
                }

                budgetBytes = GetBudgetBytes();
                if (budgetBytes <= 0L)
                {
                    Publish("disabled", budgetBytes, bytesRead, filesRead, startedAt);
                    return;
                }

                DdsCacheContract.PublishReadAhead(
                    "loading-index",
                    budgetBytes,
                    0L,
                    0,
                    0.0);
                string saveDataFolder = PreloaderPaths.FindSaveDataFolder();
                string cacheRoot = FindCacheRoot(saveDataFolder);
                string indexPath = Path.Combine(cacheRoot, DdsCacheContract.IndexFileName);
                if (!File.Exists(indexPath))
                {
                    Publish("missing-index", budgetBytes, bytesRead, filesRead, startedAt);
                    return;
                }

                FileInfo indexFile = new FileInfo(indexPath);
                if (indexFile.Length <= 0L || indexFile.Length > MaximumIndexBytes)
                {
                    Publish("invalid-index", budgetBytes, bytesRead, filesRead, startedAt);
                    return;
                }

                byte[] indexBytes = ReadIndex(indexPath, checked((int)indexFile.Length));
                DdsCacheContract.PublishIndex(
                    indexPath,
                    indexFile.Length,
                    indexFile.LastWriteTimeUtc.Ticks,
                    indexBytes);
                TextureCacheManifest manifest = ReadManifest(indexBytes);
                if (manifest == null ||
                    manifest.SchemaVersion != DdsCacheContract.ManifestSchemaVersion ||
                    !string.Equals(
                        manifest.CacheIdentity,
                        DdsCacheContract.CacheIdentityVersion,
                        StringComparison.Ordinal) ||
                    manifest.Entries == null)
                {
                    Publish("invalid-index", budgetBytes, bytesRead, filesRead, startedAt);
                    return;
                }

                IReadOnlyDictionary<string, int> modOrder = ReadActiveModOrder(saveDataFolder);
                byte[] buffer = new byte[ReadBufferBytes];
                DdsCacheContract.PublishReadAhead(
                    "running",
                    budgetBytes,
                    0L,
                    0,
                    ElapsedMilliseconds(startedAt));
                HashSet<string> visitedPaths = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (TextureCacheManifestEntry entry in OrderEntries(
                             manifest.Entries,
                             modOrder))
                {
                    if (DdsCacheContract.IsReadAheadStopRequested())
                    {
                        Publish("cancelled", budgetBytes, bytesRead, filesRead, startedAt);
                        return;
                    }

                    if (!TryResolveCachePath(cacheRoot, entry, out string path))
                    {
                        continue;
                    }

                    if (!visitedPaths.Add(path))
                    {
                        continue;
                    }

                    long read;
                    try
                    {
                        FileInfo file = new FileInfo(path);
                        long remaining = budgetBytes - bytesRead;
                        if (!file.Exists || file.Length <= 0L || remaining <= 0L)
                        {
                            continue;
                        }

                        read = ReadFile(path, buffer, remaining);
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    if (read <= 0L)
                    {
                        continue;
                    }

                    bytesRead += read;
                    if (DdsCacheContract.IsReadAheadStopRequested())
                    {
                        Publish("cancelled", budgetBytes, bytesRead, filesRead, startedAt);
                        return;
                    }

                    filesRead++;
                    if ((filesRead & 63) == 0)
                    {
                        DdsCacheContract.PublishReadAhead(
                            "running",
                            budgetBytes,
                            bytesRead,
                            filesRead,
                            ElapsedMilliseconds(startedAt));
                    }

                    if (bytesRead >= budgetBytes)
                    {
                        break;
                    }
                }

                Publish("completed", budgetBytes, bytesRead, filesRead, startedAt);
            }
            catch (Exception exception)
            {
                DdsCacheContract.PublishReadAhead(
                    "failed",
                    budgetBytes,
                    bytesRead,
                    filesRead,
                    ElapsedMilliseconds(startedAt),
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static byte[] ReadIndex(string path, int length)
        {
            byte[] bytes = new byte[length];
            int offset = 0;
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete,
                       64 * 1024,
                       FileOptions.SequentialScan))
            {
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("DDS cache index ended early.");
                    }

                    offset += read;
                }
            }

            return bytes;
        }

        private static TextureCacheManifest ReadManifest(byte[] bytes)
        {
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(TextureCacheManifest));
            using (MemoryStream stream = new MemoryStream(bytes, writable: false))
            {
                return serializer.ReadObject(stream) as TextureCacheManifest;
            }
        }

        private static long ReadFile(
            string path,
            byte[] buffer,
            long maximumBytes)
        {
            long total = 0L;
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete,
                       buffer.Length,
                       FileOptions.SequentialScan))
            {
                while (total < maximumBytes)
                {
                    int requested = (int)Math.Min(
                        buffer.Length,
                        maximumBytes - total);
                    int read = stream.Read(buffer, 0, requested);
                    if (read <= 0)
                    {
                        break;
                    }

                    total += read;
                    if (DdsCacheContract.IsReadAheadStopRequested())
                    {
                        break;
                    }
                }
            }

            return total;
        }

        private static IEnumerable<TextureCacheManifestEntry> OrderEntries(
            IEnumerable<TextureCacheManifestEntry> entries,
            IReadOnlyDictionary<string, int> modOrder)
        {
            return entries
                .Where(entry =>
                    entry != null &&
                    (modOrder.Count == 0 ||
                     modOrder.ContainsKey(entry.PackageId ?? string.Empty)))
                .OrderBy(entry =>
                    modOrder.TryGetValue(entry.PackageId ?? string.Empty, out int order)
                        ? order
                        : int.MaxValue)
                .ThenBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.SourcePath, StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, int> ReadActiveModOrder(
            string saveDataFolder)
        {
            Dictionary<string, int> result =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(saveDataFolder))
            {
                return result;
            }

            string path = Path.Combine(saveDataFolder, "Config", "ModsConfig.xml");
            if (!File.Exists(path))
            {
                return result;
            }

            try
            {
                XmlReaderSettings settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                XmlDocument document = new XmlDocument { XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(path, settings))
                {
                    document.Load(reader);
                }

                XmlNodeList nodes = document.SelectNodes("/ModsConfigData/activeMods/li");
                if (nodes == null)
                {
                    return result;
                }

                foreach (XmlNode node in nodes)
                {
                    string packageId = node.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(packageId) &&
                        !result.ContainsKey(packageId))
                    {
                        result.Add(packageId, result.Count);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (XmlException)
            {
            }

            return result;
        }

        private static bool TryResolveCachePath(
            string cacheRoot,
            TextureCacheManifestEntry entry,
            out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(entry.CachePath) || entry.CacheBytes <= 0L)
            {
                return false;
            }

            string root = Path.GetFullPath(cacheRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(cacheRoot, entry.CachePath));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Path.GetExtension(candidate),
                    DdsCacheContract.PackFileExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = candidate;
            return true;
        }

        private static string FindCacheRoot(string saveDataFolder)
        {
            string configured = Environment.GetEnvironmentVariable(
                DdsCacheContract.CacheRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            return Path.Combine(
                saveDataFolder,
                "FixWorld",
                "TextureCache",
                DdsCacheContract.CacheDirectoryName);
        }

        private static long GetBudgetBytes()
        {
            string configured = Environment.GetEnvironmentVariable(
                DdsCacheContract.ReadAheadMiBEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured) &&
                long.TryParse(
                    configured,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long configuredMiB))
            {
                return Math.Max(0L, Math.Min(configuredMiB, 8192L)) * MiB;
            }

            long availableBytes = GetAvailablePhysicalMemory();
            return availableBytes > 0L
                ? Math.Min(DefaultMaximumBudgetBytes, availableBytes / 8L)
                : DefaultMaximumBudgetBytes;
        }

        private static long GetAvailablePhysicalMemory()
        {
            MemoryStatus status = new MemoryStatus
            {
                Length = (uint)Marshal.SizeOf(typeof(MemoryStatus))
            };
            if (!GlobalMemoryStatusEx(ref status))
            {
                return 0L;
            }

            return status.AvailablePhysical > long.MaxValue
                ? long.MaxValue
                : (long)status.AvailablePhysical;
        }

        private static void Publish(
            string status,
            long budgetBytes,
            long bytesRead,
            int filesRead,
            long startedAt)
        {
            DdsCacheContract.PublishReadAhead(
                status,
                budgetBytes,
                bytesRead,
                filesRead,
                ElapsedMilliseconds(startedAt));
        }

        private static double ElapsedMilliseconds(long startedAt)
        {
            return (Stopwatch.GetTimestamp() - startedAt) * 1000.0 /
                   Stopwatch.Frequency;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatus
        {
            internal uint Length;
            internal uint MemoryLoad;
            internal ulong TotalPhysical;
            internal ulong AvailablePhysical;
            internal ulong TotalPageFile;
            internal ulong AvailablePageFile;
            internal ulong TotalVirtual;
            internal ulong AvailableVirtual;
            internal ulong AvailableExtendedVirtual;
        }
    }
}
