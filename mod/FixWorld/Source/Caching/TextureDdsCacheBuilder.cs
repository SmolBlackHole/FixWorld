using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FixWorld.Caching
{
    internal sealed class TextureDdsCacheBuilder
    {
        private const int ConversionTimeoutMilliseconds = 10 * 60 * 1000;

        private readonly string cacheRoot;
        private readonly string texconvPath;
        private readonly string identity;

        internal TextureDdsCacheBuilder(string cacheRoot, string modRoot)
        {
            this.cacheRoot = cacheRoot;
            texconvPath = FindTexconv(modRoot);
            identity = File.Exists(texconvPath) ? GetFileIdentity(texconvPath) : null;
        }

        internal bool Available => !string.IsNullOrEmpty(texconvPath);

        internal string TexconvPath => texconvPath;

        internal string Identity => identity;

        internal CacheBuildResult Build(IReadOnlyList<TextureCacheEntry> entries)
        {
            return Publish(Prepare(entries, CancellationToken.None));
        }

        internal CacheBuildPreparation Prepare(IReadOnlyList<TextureCacheEntry> entries)
        {
            return Prepare(entries, CancellationToken.None);
        }

        internal CacheBuildPreparation Prepare(
            IReadOnlyList<TextureCacheEntry> entries,
            CancellationToken cancellationToken)
        {
            if (entries.Count == 0)
            {
                return CacheBuildPreparation.Empty;
            }

            if (!Available)
            {
                return new CacheBuildPreparation(
                    null,
                    Array.Empty<CacheBuildArtifact>(),
                    entries.Count,
                    0.0,
                    null);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            int failed = 0;
            List<string> errors = [];
            List<CacheBuildArtifact> artifacts = new List<CacheBuildArtifact>(entries.Count);
            string stagingRoot = Path.Combine(
                cacheRoot,
                ".staging-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "-" +
                Guid.NewGuid().ToString("N"));
            EnsureChildPath(cacheRoot, stagingRoot);

            try
            {
                foreach (TextureCacheEntry entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string inputDirectory = Path.Combine(stagingRoot, "input", entry.MipCount.ToString(CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(inputDirectory);
                    string inputPath = Path.Combine(inputDirectory, entry.Hash + entry.Source.Extension.ToLowerInvariant());
                    File.Copy(entry.Source.FullName, inputPath, true);
                }

                foreach (IGrouping<int, TextureCacheEntry> group in entries.GroupBy(entry => entry.MipCount))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string groupName = group.Key.ToString(CultureInfo.InvariantCulture);
                    string inputPattern = Path.Combine(stagingRoot, "input", groupName, "*.*");
                    string outputDirectory = Path.Combine(stagingRoot, "output", groupName);
                    Directory.CreateDirectory(outputDirectory);

                    string error = RunTexconv(
                        inputPattern,
                        outputDirectory,
                        group.Key,
                        cancellationToken);
                    if (error != null)
                    {
                        int groupCount = group.Count();
                        failed += groupCount;
                        errors.Add(error);
                        continue;
                    }

                    foreach (TextureCacheEntry entry in group)
                    {
                        string convertedPath = Path.Combine(outputDirectory, entry.Hash + ".DDS");
                        if (!File.Exists(convertedPath))
                        {
                            failed++;
                            errors.Add("Expected DDS file is missing: " + convertedPath);
                            continue;
                        }

                        artifacts.Add(new CacheBuildArtifact(entry, convertedPath));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                DeleteStagingDirectory(stagingRoot);
                throw;
            }
            catch (Exception exception)
            {
                failed = Math.Max(failed, entries.Count - artifacts.Count);
                errors.Add(exception.Message);
            }
            finally
            {
                stopwatch.Stop();
            }

            return new CacheBuildPreparation(
                stagingRoot,
                artifacts,
                failed,
                stopwatch.Elapsed.TotalMilliseconds,
                errors.Count == 0 ? null : string.Join(" | ", errors.Take(3)));
        }

        internal CacheBuildResult Publish(CacheBuildPreparation preparation)
        {
            if (preparation == null)
            {
                throw new ArgumentNullException(nameof(preparation));
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            int created = 0;
            long createdBytes = 0L;
            int failed = preparation.Failed;
            List<string> errors = new List<string>();
            if (preparation.Error != null)
            {
                errors.Add(preparation.Error);
            }

            try
            {
                foreach (CacheBuildArtifact artifact in preparation.Artifacts)
                {
                    try
                    {
                        TextureCacheEntry entry = artifact.Entry;
                        Directory.CreateDirectory(entry.FinalDirectory);
                        if (File.Exists(entry.FinalPath))
                        {
                            File.Delete(artifact.StagedPath);
                            continue;
                        }

                        File.Move(artifact.StagedPath, entry.FinalPath);
                        created++;
                        createdBytes += new FileInfo(entry.FinalPath).Length;
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        errors.Add(exception.Message);
                    }
                }
            }
            finally
            {
                stopwatch.Stop();
                string cleanupError = DeleteStagingDirectory(preparation.StagingRoot);
                if (cleanupError != null)
                {
                    errors.Add(cleanupError);
                }
            }

            return new CacheBuildResult(
                created,
                createdBytes,
                failed,
                preparation.Milliseconds + stopwatch.Elapsed.TotalMilliseconds,
                errors.Count == 0 ? null : string.Join(" | ", errors.Take(3)));
        }

        private static string DeleteStagingDirectory(string stagingRoot)
        {
            if (string.IsNullOrEmpty(stagingRoot) || !Directory.Exists(stagingRoot))
            {
                return null;
            }

            try
            {
                Directory.Delete(stagingRoot, true);
                return null;
            }
            catch (IOException exception)
            {
                return "The staging directory could not be deleted: " + exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                return "The staging directory could not be deleted: " + exception.Message;
            }
        }

        private string RunTexconv(
            string inputPattern,
            string outputDirectory,
            int mipCount,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = texconvPath,
                Arguments = "-nologo -y -vflip -f BC3_UNORM -m " +
                            mipCount.ToString(CultureInfo.InvariantCulture) +
                            " -o " + Quote(outputDirectory) + " " + Quote(inputPattern),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            StringBuilder output = new();
            StringBuilder error = new();
            using Process process = new() { StartInfo = startInfo };
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    output.AppendLine(eventArgs.Data);
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    error.AppendLine(eventArgs.Data);
                }
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Stopwatch timeout = Stopwatch.StartNew();
            while (!process.WaitForExit(100))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (timeout.ElapsedMilliseconds >= ConversionTimeoutMilliseconds)
                {
                    TryKill(process);
                    return "texconv exceeded its time limit";
                }
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string detail = error.Length > 0 ? error.ToString() : output.ToString();
                return "texconv failed with exit code " +
                       process.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + detail.Trim();
            }

            return null;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
            catch
            {
                // The process may exit between the state check and Kill().
            }
        }

        private static string FindTexconv(string modRoot)
        {
            string configuredPath = Environment.GetEnvironmentVariable("FIXWORLD_TEXCONV_PATH");
            if (File.Exists(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            bool isWindows = IsWindows();
            string executableName = isWindows ? "texconv.exe" : "texconv";
            if (isWindows)
            {
                string bundledPath = Path.Combine(
                    modRoot,
                    "Tools",
                    "Windows-x64",
                    executableName);
                if (File.Exists(bundledPath))
                {
                    return bundledPath;
                }
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string candidate = Path.Combine(directory.Trim().Trim('"'), executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            if (!isWindows)
            {
                return null;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string packagesRoot = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (!Directory.Exists(packagesRoot))
            {
                return null;
            }

            try
            {
                return Directory
                    .EnumerateDirectories(packagesRoot, "Microsoft.DirectXTex.Texconv_*")
                    .Select(directory => Path.Combine(directory, "texconv.exe"))
                    .FirstOrDefault(File.Exists);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool IsWindows()
        {
            PlatformID platform = Environment.OSVersion.Platform;
            return platform == PlatformID.Win32NT ||
                   platform == PlatformID.Win32S ||
                   platform == PlatformID.Win32Windows ||
                   platform == PlatformID.WinCE;
        }

        private static string GetFileIdentity(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = new(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read);
            byte[] hash = sha256.ComputeHash(stream);
            return "sha256:" + BitConverter.ToString(hash)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static void EnsureChildPath(string parent, string child)
        {
            string resolvedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) +
                                    Path.DirectorySeparatorChar;
            string resolvedChild = Path.GetFullPath(child);
            if (!resolvedChild.StartsWith(resolvedParent, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid cache path: " + resolvedChild);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class TextureCacheEntry
    {
        internal readonly string Key;
        internal readonly string PackageId;
        internal readonly string SourcePath;
        internal readonly string SourceHash;
        internal readonly string ConverterIdentity;
        internal readonly FileInfo Source;
        internal readonly string Hash;
        internal readonly int MipCount;
        internal readonly long EstimatedCacheBytes;
        internal readonly string FinalDirectory;
        internal readonly string FinalPath;

        internal TextureCacheEntry(
            string key,
            string packageId,
            string sourcePath,
            string sourceHash,
            string converterIdentity,
            FileInfo source,
            string hash,
            int mipCount,
            long estimatedCacheBytes,
            string finalDirectory,
            string finalPath)
        {
            Key = key;
            PackageId = packageId;
            SourcePath = sourcePath;
            SourceHash = sourceHash;
            ConverterIdentity = converterIdentity;
            Source = source;
            Hash = hash;
            MipCount = mipCount;
            EstimatedCacheBytes = estimatedCacheBytes;
            FinalDirectory = finalDirectory;
            FinalPath = finalPath;
        }
    }

    internal sealed class CacheBuildPreparation
    {
        internal static readonly CacheBuildPreparation Empty = new(
            null,
            Array.Empty<CacheBuildArtifact>(),
            0,
            0.0,
            null);

        internal readonly string StagingRoot;
        internal readonly IReadOnlyList<CacheBuildArtifact> Artifacts;
        internal readonly int Failed;
        internal readonly double Milliseconds;
        internal readonly string Error;

        internal CacheBuildPreparation(
            string stagingRoot,
            IReadOnlyList<CacheBuildArtifact> artifacts,
            int failed,
            double milliseconds,
            string error)
        {
            StagingRoot = stagingRoot;
            Artifacts = artifacts;
            Failed = failed;
            Milliseconds = milliseconds;
            Error = error;
        }
    }

    internal readonly struct CacheBuildArtifact
    {
        internal readonly TextureCacheEntry Entry;
        internal readonly string StagedPath;

        internal CacheBuildArtifact(TextureCacheEntry entry, string stagedPath)
        {
            Entry = entry;
            StagedPath = stagedPath;
        }
    }

    internal readonly struct CacheBuildResult
    {
        internal static readonly CacheBuildResult Empty = new(0, 0L, 0, 0.0, null);

        internal readonly int Created;
        internal readonly long CreatedBytes;
        internal readonly int Failed;
        internal readonly double Milliseconds;
        internal readonly string Error;

        internal CacheBuildResult(int created, long createdBytes, int failed, double milliseconds, string error)
        {
            Created = created;
            CreatedBytes = createdBytes;
            Failed = failed;
            Milliseconds = milliseconds;
            Error = error;
        }
    }
}
