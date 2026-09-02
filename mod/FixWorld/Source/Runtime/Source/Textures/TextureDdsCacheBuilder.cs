using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using FixWorld.ExternalTools;

namespace FixWorld.Textures
{
    internal sealed class TextureDdsCacheBuilder
    {
        private readonly string cacheRoot;
        private readonly string texconvPath;
        private readonly string identity;

        internal TextureDdsCacheBuilder(string cacheRoot, string modRoot)
        {
            this.cacheRoot = cacheRoot;
            texconvPath = TexconvProcess.FindExecutable(Path.Combine(
                modRoot,
                "Tools",
                "Windows-x64"));
            identity = File.Exists(texconvPath)
                ? TextureCacheIdentity.GetConverterIdentity(texconvPath)
                : null;
        }

        internal bool Available => !string.IsNullOrEmpty(texconvPath);

        internal string TexconvPath => texconvPath;

        internal string Identity => identity;

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
                    null);
            }

            int failed = 0;
            List<string> errors = [];
            List<CacheBuildArtifact> artifacts = new List<CacheBuildArtifact>(entries.Count);
            string stagingRoot = Path.Combine(
                cacheRoot,
                ".staging-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "-" +
                Guid.NewGuid().ToString("N"));
            TextureCacheIdentity.EnsureChildPath(cacheRoot, stagingRoot);

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
                    string[] inputPaths = group
                        .Select(entry => Path.Combine(
                            stagingRoot,
                            "input",
                            groupName,
                            entry.Hash + entry.Source.Extension.ToLowerInvariant()))
                        .ToArray();
                    string outputDirectory = Path.Combine(stagingRoot, "output", groupName);
                    Directory.CreateDirectory(outputDirectory);

                    string error = RunTexconv(
                        inputPaths,
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
            return new CacheBuildPreparation(
                stagingRoot,
                artifacts,
                failed,
                errors.Count == 0 ? null : string.Join(" | ", errors.Take(3)));
        }

        internal CacheBuildResult Publish(CacheBuildPreparation preparation)
        {
            if (preparation == null)
            {
                throw new ArgumentNullException(nameof(preparation));
            }

            int created = 0;
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
                string cleanupError = DeleteStagingDirectory(preparation.StagingRoot);
                if (cleanupError != null)
                {
                    errors.Add(cleanupError);
                }
            }

            return new CacheBuildResult(
                created,
                failed,
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
            IReadOnlyList<string> inputPaths,
            string outputDirectory,
            int mipCount,
            CancellationToken cancellationToken)
        {
            TexconvProcessResult result = TexconvProcess.Run(
                texconvPath,
                outputDirectory,
                inputPaths,
                mipCount,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                string detail = !string.IsNullOrWhiteSpace(result.Error)
                    ? result.Error
                    : result.Output;
                return "texconv failed with exit code " +
                       result.ExitCode.ToString(CultureInfo.InvariantCulture) +
                       ": " + detail.Trim();
            }

            return null;
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
            null);

        internal readonly string StagingRoot;
        internal readonly IReadOnlyList<CacheBuildArtifact> Artifacts;
        internal readonly int Failed;
        internal readonly string Error;

        internal CacheBuildPreparation(
            string stagingRoot,
            IReadOnlyList<CacheBuildArtifact> artifacts,
            int failed,
            string error)
        {
            StagingRoot = stagingRoot;
            Artifacts = artifacts;
            Failed = failed;
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
        internal readonly int Created;
        internal readonly int Failed;
        internal readonly string Error;

        internal CacheBuildResult(int created, int failed, string error)
        {
            Created = created;
            Failed = failed;
            Error = error;
        }
    }
}
