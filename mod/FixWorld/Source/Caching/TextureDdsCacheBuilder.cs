using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FixWorld.Caching
{
    internal sealed class TextureDdsCacheBuilder
    {
        private const int ConversionTimeoutMilliseconds = 10 * 60 * 1000;

        private readonly string cacheRoot;
        private readonly string texconvPath;

        internal TextureDdsCacheBuilder(string cacheRoot, string modRoot)
        {
            this.cacheRoot = cacheRoot;
            texconvPath = FindTexconv(modRoot);
        }

        internal bool Available => !string.IsNullOrEmpty(texconvPath);

        internal string TexconvPath => texconvPath;

        internal CacheBuildResult Build(IReadOnlyList<TextureCacheEntry> entries)
        {
            if (entries.Count == 0)
            {
                return CacheBuildResult.Empty;
            }

            if (!Available)
            {
                return new CacheBuildResult(0, 0L, entries.Count, 0.0, null);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            int created = 0;
            long createdBytes = 0L;
            int failed = 0;
            List<string> errors = new List<string>();
            string stagingRoot = Path.Combine(
                cacheRoot,
                ".staging-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "-" +
                Guid.NewGuid().ToString("N"));
            EnsureChildPath(cacheRoot, stagingRoot);

            try
            {
                foreach (TextureCacheEntry entry in entries)
                {
                    string inputDirectory = Path.Combine(stagingRoot, "input", entry.MipCount.ToString(CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(inputDirectory);
                    string inputPath = Path.Combine(inputDirectory, entry.Hash + entry.Source.Extension.ToLowerInvariant());
                    File.Copy(entry.Source.FullName, inputPath, true);
                }

                foreach (IGrouping<int, TextureCacheEntry> group in entries.GroupBy(entry => entry.MipCount))
                {
                    string groupName = group.Key.ToString(CultureInfo.InvariantCulture);
                    string inputPattern = Path.Combine(stagingRoot, "input", groupName, "*.*");
                    string outputDirectory = Path.Combine(stagingRoot, "output", groupName);
                    Directory.CreateDirectory(outputDirectory);

                    string error = RunTexconv(inputPattern, outputDirectory, group.Key);
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

                        Directory.CreateDirectory(entry.FinalDirectory);
                        if (File.Exists(entry.FinalPath))
                        {
                            File.Delete(convertedPath);
                        }
                        else
                        {
                            File.Move(convertedPath, entry.FinalPath);
                            created++;
                            createdBytes += new FileInfo(entry.FinalPath).Length;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                failed = Math.Max(failed, entries.Count - created);
                errors.Add(exception.Message);
            }
            finally
            {
                stopwatch.Stop();
                if (Directory.Exists(stagingRoot))
                {
                    try
                    {
                        Directory.Delete(stagingRoot, true);
                    }
                    catch (IOException exception)
                    {
                        errors.Add("The staging directory could not be deleted: " + exception.Message);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        errors.Add("The staging directory could not be deleted: " + exception.Message);
                    }
                }
            }

            return new CacheBuildResult(
                created,
                createdBytes,
                failed,
                stopwatch.Elapsed.TotalMilliseconds,
                errors.Count == 0 ? null : string.Join(" | ", errors.Take(3)));
        }

        private string RunTexconv(string inputPattern, string outputDirectory, int mipCount)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = texconvPath,
                Arguments = "-nologo -y -f BC3_UNORM -m " +
                            mipCount.ToString(CultureInfo.InvariantCulture) +
                            " -o " + Quote(outputDirectory) + " " + Quote(inputPattern),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            StringBuilder output = new StringBuilder();
            StringBuilder error = new StringBuilder();
            using (Process process = new Process { StartInfo = startInfo })
            {
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
                if (!process.WaitForExit(ConversionTimeoutMilliseconds))
                {
                    process.Kill();
                    return "texconv exceeded its time limit";
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string detail = error.Length > 0 ? error.ToString() : output.ToString();
                    return "texconv failed with exit code " +
                           process.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + detail.Trim();
                }
            }

            return null;
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
        internal readonly FileInfo Source;
        internal readonly string Hash;
        internal readonly int MipCount;
        internal readonly long EstimatedCacheBytes;
        internal readonly string FinalDirectory;
        internal readonly string FinalPath;

        internal TextureCacheEntry(
            string key,
            FileInfo source,
            string hash,
            int mipCount,
            long estimatedCacheBytes,
            string finalDirectory,
            string finalPath)
        {
            Key = key;
            Source = source;
            Hash = hash;
            MipCount = mipCount;
            EstimatedCacheBytes = estimatedCacheBytes;
            FinalDirectory = finalDirectory;
            FinalPath = finalPath;
        }
    }

    internal readonly struct CacheBuildResult
    {
        internal static readonly CacheBuildResult Empty = new CacheBuildResult(0, 0L, 0, 0.0, null);

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
