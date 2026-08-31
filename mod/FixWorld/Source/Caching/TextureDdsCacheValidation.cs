using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FixWorld.Loading;
using UnityEngine;
using Verse;

namespace FixWorld.Caching
{
    internal static partial class TextureDdsCache
    {
        private static readonly Dictionary<string, TextureCacheValidationResult>
            PreparedValidations =
                new Dictionary<string, TextureCacheValidationResult>(StringComparer.Ordinal);
        private static readonly List<TextureCacheEntry> DeferredBuildEntries =
            new List<TextureCacheEntry>();

        internal static bool TryCreateValidationPlan(
            IReadOnlyList<ModContentPack> mods,
            out LoadingActionPlan plan)
        {
            plan = default;
            if (!enabled ||
                workerCount <= 0 ||
                index == null ||
                index.RecoveryRequired ||
                !Prefs.TextureCompression ||
                mods == null ||
                mods.Count == 0)
            {
                return false;
            }

            TextureCacheValidationIndex validationIndex;
            TextureCacheValidationTarget[] targets;
            TextureCacheBuildBudget buildBudget;
            lock (Sync)
            {
                validationIndex = index.CreateValidationSnapshot();
                targets = mods
                    .Select(CreateValidationTarget)
                    .ToArray();
                buildBudget = new TextureCacheBuildBudget(
                    index.CurrentBytes,
                    new DriveInfo(Path.GetPathRoot(cacheRoot)).AvailableFreeSpace,
                    maxCacheBytes,
                    minimumFreeBytes,
                    builder.Available);
            }

            LoadingWorkItem[] validationTasks = new LoadingWorkItem[targets.Length];
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                TextureCacheValidationTarget target = targets[targetIndex];
                int current = targetIndex + 1;
                validationTasks[targetIndex] = LoadingWorkItem.CreateParallelThenCommit(
                    LoadingStage.Content,
                    LoadingStep.ValidateTextureCache,
                    "Prepare texture cache",
                    "Discovering and validating textures for " + target.ModName,
                    target.ModName,
                    LoadingModAttribution.Exact(target.PackageId, target.ModName),
                    current,
                    targets.Length,
                    continueOnFailure: true,
                    prepare: () => PrepareValidation(target, validationIndex),
                    commit: result => StorePreparedValidation(result, buildBudget));
            }

            LoadingPipelineStage stage = new LoadingPipelineStage(
                0,
                "Prepare texture cache",
                LoadingStage.Content,
                LoadingStep.ValidateTextureCache,
                LoadingExecutionMode.ParallelThenCommit,
                validationTasks,
                maxParallelism: Math.Min(workerCount, 4));
            plan = new LoadingActionPlan(
                "FixWorld texture discovery and DDS validation",
                LoadingModAttribution.Global,
                stage);
            return true;
        }

        internal static bool TryGetPreparedFiles(
            ModContentPack mod,
            string contentPath,
            Func<string, bool> validateExtension,
            out Dictionary<string, FileInfo> files)
        {
            files = null;
            if (!IsStandardTextureRequest(contentPath, validateExtension))
            {
                return false;
            }

            lock (Sync)
            {
                string packageId = Normalize(mod.PackageId);
                if (!PreparedValidations.TryGetValue(
                        packageId,
                        out TextureCacheValidationResult prepared))
                {
                    return false;
                }

                files = prepared.Files;
                return true;
            }
        }

        private static TextureCacheValidationTarget CreateValidationTarget(
            ModContentPack mod)
        {
            string root = Path.GetFullPath(mod.RootDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            return new TextureCacheValidationTarget(
                Normalize(mod.PackageId),
                mod.Name,
                root,
                mod.foldersToLoadDescendingOrder.ToArray(),
                GenFilePaths.ContentPath<Texture2D>(),
                builder.Identity);
        }

        private static TextureCacheValidationResult PrepareValidation(
            TextureCacheValidationTarget target,
            TextureCacheValidationIndex validationIndex)
        {
            LoadingStageOperation operation = LoadingStageMailbox.Begin(
                Descriptor(
                    LoadingStage.Content,
                    LoadingStep.ValidateTextureCache,
                    "Prepare texture cache",
                    "Discovering textures for " + target.ModName,
                    LoadingModAttribution.Exact(target.PackageId, target.ModName),
                    target.PackageId,
                    LoadingThreadAffinity.WorkerSafe));
            try
            {
                Dictionary<string, FileInfo> files = ModFileLoader.DiscoverTextures(
                    target.Folders,
                    target.ContentPath);
                HashSet<string> shippedDdsPaths = new HashSet<string>(
                    files.Keys
                        .Select(Normalize)
                        .Where(path => path.EndsWith(".dds", StringComparison.Ordinal)),
                    StringComparer.Ordinal);
                List<TextureCacheValidationDecision> decisions =
                    new List<TextureCacheValidationDecision>();
                List<TextureCacheEntry> missingEntries = new List<TextureCacheEntry>();
                List<KeyValuePair<string, FileInfo>> sourceFiles = files.ToList();

                for (int sourceIndex = 0; sourceIndex < sourceFiles.Count; sourceIndex++)
                {
                    KeyValuePair<string, FileInfo> item = sourceFiles[sourceIndex];
                    operation.ReportProgress(
                        sourceIndex + 1,
                        sourceFiles.Count,
                        "Checking cached textures for " + target.ModName);
                    FileInfo source = item.Value;
                    string sourceKey = Normalize(item.Key);
                    string extension = source.Extension.ToLowerInvariant();
                    if (extension == ".dds" ||
                        shippedDdsPaths.Contains(Path.ChangeExtension(sourceKey, ".dds")) ||
                        !source.FullName.StartsWith(
                            target.ModRoot,
                            StringComparison.OrdinalIgnoreCase) ||
                        extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                    {
                        continue;
                    }

                    string sourcePath = GetRelativeSourcePath(source, target.ModRoot);
                    if (validationIndex.TryGetFresh(
                            target.PackageId,
                            sourcePath,
                            source,
                            target.ConverterIdentity,
                            out string cachePath))
                    {
                        decisions.Add(TextureCacheValidationDecision.Fresh(
                            item.Key,
                            source,
                            sourcePath,
                            cachePath));
                        continue;
                    }

                    if (!TextureDimensions.TryRead(source, out TextureDimensions dimensions))
                    {
                        decisions.Add(TextureCacheValidationDecision.Unsupported(
                            item.Key,
                            source,
                            sourcePath));
                        continue;
                    }

                    int mipCount = dimensions.GetBc3MipCount();
                    if (mipCount == 0)
                    {
                        decisions.Add(TextureCacheValidationDecision.Excluded(
                            item.Key,
                            source,
                            sourcePath));
                        continue;
                    }

                    string sourceHash = GetFileHash(source.FullName);
                    if (validationIndex.TryGetReusable(
                            target.PackageId,
                            sourcePath,
                            sourceHash,
                            target.ConverterIdentity,
                            out string reusablePath))
                    {
                        decisions.Add(TextureCacheValidationDecision.Reused(
                            item.Key,
                            source,
                            sourcePath,
                            sourceHash,
                            reusablePath));
                        continue;
                    }

                    string cacheKey = GetContentCacheKey(
                        sourcePath,
                        sourceHash,
                        target.ConverterIdentity);
                    string finalDirectory = Path.Combine(
                        cacheRoot,
                        SanitizePathSegment(target.PackageId),
                        cacheKey);
                    string finalPath = Path.Combine(
                        finalDirectory,
                        Path.GetFileNameWithoutExtension(source.Name) + ".dds");
                    TextureCacheEntry entry = new TextureCacheEntry(
                        item.Key,
                        target.PackageId,
                        sourcePath,
                        sourceHash,
                        target.ConverterIdentity,
                        source,
                        cacheKey,
                        mipCount,
                        dimensions.GetBc3FileSize(mipCount),
                        finalDirectory,
                        finalPath);
                    if (File.Exists(finalPath))
                    {
                        decisions.Add(TextureCacheValidationDecision.Existing(entry));
                        continue;
                    }

                    decisions.Add(TextureCacheValidationDecision.Missing(entry));
                    missingEntries.Add(entry);
                    Interlocked.Increment(ref missCount);
                }

                return new TextureCacheValidationResult(
                    target.PackageId,
                    target.ModRoot,
                    files,
                    decisions,
                    missingEntries,
                    complete: missingEntries.Count == 0);
            }
            catch
            {
                operation.Fail();
                throw;
            }
            finally
            {
                operation.Dispose();
            }
        }

        private static void StorePreparedValidation(
            TextureCacheValidationResult result,
            TextureCacheBuildBudget buildBudget)
        {
            lock (Sync)
            {
                IReadOnlyList<TextureCacheEntry> selected =
                    buildBudget.Select(result.MissingEntries);
                Interlocked.Add(
                    ref budgetSkippedCount,
                    result.MissingEntries.Count - selected.Count);
                DeferredBuildEntries.AddRange(selected);
                result = result.DeferBuildEntries();
                foreach (TextureCacheValidationDecision decision in result.Decisions)
                {
                    if (decision.UsesCache)
                    {
                        result.Files[decision.SourceKey] = new FileInfo(decision.CachePath);
                    }
                }
                PreparedValidations[result.PackageId] = result;
                Interlocked.Increment(ref workerPreparedMods);
            }
        }

        private static bool TryApplyPrepared(
            ModContentPack mod,
            Dictionary<string, FileInfo> files)
        {
            string packageId = Normalize(mod.PackageId);
            if (!PreparedValidations.TryGetValue(
                    packageId,
                    out TextureCacheValidationResult prepared))
            {
                return false;
            }

            PreparedValidations.Remove(packageId);
            if (CommitPreparedValidation(mod, files, prepared))
            {
                Interlocked.Increment(ref workerAppliedMods);
                return true;
            }

            Interlocked.Increment(ref workerFallbackMods);
            return false;
        }

        private static bool HasCompletePreparedValidation(ModContentPack mod)
        {
            return PreparedValidations.TryGetValue(
                       Normalize(mod.PackageId),
                       out TextureCacheValidationResult prepared) &&
                   prepared.Complete;
        }

        private static bool CommitPreparedValidation(
            ModContentPack mod,
            Dictionary<string, FileInfo> files,
            TextureCacheValidationResult prepared)
        {
            if (!prepared.Complete ||
                !string.Equals(
                    prepared.PackageId,
                    Normalize(mod.PackageId),
                    StringComparison.Ordinal))
            {
                return false;
            }

            IReadOnlyList<KeyValuePair<string, TextureCacheValidationDecision>> commits;
            if (ReferenceEquals(files, prepared.Files))
            {
                commits = prepared.Decisions
                    .Select(decision =>
                        new KeyValuePair<string, TextureCacheValidationDecision>(
                            decision.SourceKey,
                            decision))
                    .ToArray();
            }
            else if (!TryMatchPreparedFiles(files, prepared, out commits))
            {
                return false;
            }

            HashSet<string> retainedSourcePaths = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> desiredDirectories = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, TextureCacheValidationDecision> commit in commits)
            {
                TextureCacheValidationDecision decision = commit.Value;
                switch (decision.Kind)
                {
                    case TextureCacheValidationKind.Fresh:
                        if (!ReferenceEquals(files, prepared.Files))
                        {
                            files[commit.Key] = new FileInfo(decision.CachePath);
                        }
                        index.TouchPrepared(prepared.PackageId, decision.SourcePath);
                        retainedSourcePaths.Add(decision.SourcePath);
                        desiredDirectories.Add(Path.GetDirectoryName(decision.CachePath));
                        Interlocked.Increment(ref hitCount);
                        break;
                    case TextureCacheValidationKind.Reused:
                    case TextureCacheValidationKind.Existing:
                        if (!ReferenceEquals(files, prepared.Files))
                        {
                            files[commit.Key] = new FileInfo(decision.CachePath);
                        }
                        index.RegisterExisting(
                            prepared.PackageId,
                            decision.SourcePath,
                            new FileInfo(decision.SourceFullPath),
                            decision.SourceHash,
                            builder.Identity,
                            decision.CachePath,
                            createdAfterOpen: false);
                        retainedSourcePaths.Add(decision.SourcePath);
                        desiredDirectories.Add(Path.GetDirectoryName(decision.CachePath));
                        Interlocked.Increment(ref hitCount);
                        break;
                    case TextureCacheValidationKind.Original:
                        index.RemoveSource(prepared.PackageId, decision.SourcePath);
                        break;
                    case TextureCacheValidationKind.Excluded:
                        index.RemoveSource(prepared.PackageId, decision.SourcePath);
                        Interlocked.Increment(ref excludedCount);
                        break;
                    case TextureCacheValidationKind.Unsupported:
                        index.RemoveSource(prepared.PackageId, decision.SourcePath);
                        Interlocked.Increment(ref unsupportedCount);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            int removedSources = index.RemoveMissingSources(
                prepared.PackageId,
                retainedSourcePaths);
            Interlocked.Add(ref invalidatedCount, removedSources);
            if (removedSources > 0)
            {
                string packageCacheRoot = Path.Combine(
                    cacheRoot,
                    SanitizePathSegment(prepared.PackageId));
                RemoveStaleDirectories(packageCacheRoot, desiredDirectories);
            }
            currentCacheBytes = index.CurrentBytes;
            return true;
        }

        private static bool TryMatchPreparedFiles(
            Dictionary<string, FileInfo> files,
            TextureCacheValidationResult prepared,
            out IReadOnlyList<KeyValuePair<string, TextureCacheValidationDecision>> commits)
        {
            Dictionary<string, TextureCacheValidationDecision> decisionsBySource =
                prepared.Decisions.ToDictionary(
                    decision => decision.SourceFullPath,
                    StringComparer.OrdinalIgnoreCase);
            HashSet<string> shippedDdsPaths = new HashSet<string>(
                files.Keys
                    .Select(Normalize)
                    .Where(path => path.EndsWith(".dds", StringComparison.Ordinal)),
                StringComparer.Ordinal);
            List<KeyValuePair<string, TextureCacheValidationDecision>> matched =
                new List<KeyValuePair<string, TextureCacheValidationDecision>>();

            foreach (KeyValuePair<string, FileInfo> item in files)
            {
                FileInfo source = item.Value;
                string sourceKey = Normalize(item.Key);
                string extension = source.Extension.ToLowerInvariant();
                if (extension == ".dds" ||
                    shippedDdsPaths.Contains(Path.ChangeExtension(sourceKey, ".dds")) ||
                    !source.FullName.StartsWith(
                        prepared.ModRoot,
                        StringComparison.OrdinalIgnoreCase) ||
                    extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                {
                    continue;
                }

                if (!decisionsBySource.TryGetValue(
                        source.FullName,
                        out TextureCacheValidationDecision decision) ||
                    decision.SourceLength != source.Length ||
                    decision.SourceWriteTimeUtcTicks != source.LastWriteTimeUtc.Ticks ||
                    decision.UsesCache && !File.Exists(decision.CachePath) ||
                    decision.Kind == TextureCacheValidationKind.Fresh &&
                    !index.MatchesPrepared(
                        prepared.PackageId,
                        decision.SourcePath,
                        decision.SourceLength,
                        decision.SourceWriteTimeUtcTicks,
                        builder.Identity,
                        decision.CachePath))
                {
                    commits = null;
                    return false;
                }

                matched.Add(new KeyValuePair<string, TextureCacheValidationDecision>(
                    item.Key,
                    decision));
            }

            commits = matched;
            return matched.Count == prepared.Decisions.Count;
        }

        private static bool IsStandardTextureRequest(
            string contentPath,
            Func<string, bool> validateExtension)
        {
            return string.Equals(
                       contentPath,
                       GenFilePaths.ContentPath<Texture2D>(),
                       StringComparison.Ordinal) &&
                   validateExtension != null &&
                   validateExtension(".png") &&
                   validateExtension(".dds") &&
                   !validateExtension(".txt");
        }

        private static void ClearPreparedValidations()
        {
            PreparedValidations.Clear();
        }

        private static IReadOnlyList<TextureCacheEntry> TakeDeferredBuildEntries()
        {
            TextureCacheEntry[] entries = DeferredBuildEntries.ToArray();
            DeferredBuildEntries.Clear();
            return entries;
        }

        private sealed class TextureCacheValidationTarget
        {
            internal readonly string PackageId;
            internal readonly string ModName;
            internal readonly string ModRoot;
            internal readonly string[] Folders;
            internal readonly string ContentPath;
            internal readonly string ConverterIdentity;

            internal TextureCacheValidationTarget(
                string packageId,
                string modName,
                string modRoot,
                string[] folders,
                string contentPath,
                string converterIdentity)
            {
                PackageId = packageId;
                ModName = modName;
                ModRoot = modRoot;
                Folders = folders;
                ContentPath = contentPath;
                ConverterIdentity = converterIdentity;
            }
        }

        private sealed class TextureCacheValidationResult
        {
            internal readonly string PackageId;
            internal readonly string ModRoot;
            internal readonly Dictionary<string, FileInfo> Files;
            internal readonly IReadOnlyList<TextureCacheValidationDecision> Decisions;
            internal readonly IReadOnlyList<TextureCacheEntry> MissingEntries;
            internal readonly bool Complete;

            internal TextureCacheValidationResult(
                string packageId,
                string modRoot,
                Dictionary<string, FileInfo> files,
                IReadOnlyList<TextureCacheValidationDecision> decisions,
                IReadOnlyList<TextureCacheEntry> missingEntries,
                bool complete)
            {
                PackageId = packageId;
                ModRoot = modRoot;
                Files = files;
                Decisions = decisions;
                MissingEntries = missingEntries;
                Complete = complete;
            }

            internal TextureCacheValidationResult DeferBuildEntries()
            {
                TextureCacheValidationDecision[] updated = Decisions
                    .Select(decision =>
                        decision.Kind == TextureCacheValidationKind.Missing
                            ? decision.AsOriginal()
                            : decision)
                    .ToArray();
                return new TextureCacheValidationResult(
                    PackageId,
                    ModRoot,
                    Files,
                    updated,
                    Array.Empty<TextureCacheEntry>(),
                    complete: true);
            }
        }

        private enum TextureCacheValidationKind
        {
            Fresh,
            Reused,
            Existing,
            Missing,
            Original,
            Excluded,
            Unsupported
        }

        private readonly struct TextureCacheValidationDecision
        {
            internal readonly string SourceKey;
            internal readonly string SourceFullPath;
            internal readonly string SourcePath;
            internal readonly long SourceLength;
            internal readonly long SourceWriteTimeUtcTicks;
            internal readonly string SourceHash;
            internal readonly string CachePath;
            internal readonly TextureCacheValidationKind Kind;
            internal readonly TextureCacheEntry Entry;

            internal bool UsesCache =>
                Kind == TextureCacheValidationKind.Fresh ||
                Kind == TextureCacheValidationKind.Reused ||
                Kind == TextureCacheValidationKind.Existing;

            private TextureCacheValidationDecision(
                string sourceKey,
                FileInfo source,
                string sourcePath,
                string sourceHash,
                string cachePath,
                TextureCacheValidationKind kind,
                TextureCacheEntry entry = null)
            {
                SourceKey = sourceKey;
                SourceFullPath = source.FullName;
                SourcePath = sourcePath;
                SourceLength = source.Length;
                SourceWriteTimeUtcTicks = source.LastWriteTimeUtc.Ticks;
                SourceHash = sourceHash;
                CachePath = cachePath;
                Kind = kind;
                Entry = entry;
            }

            internal static TextureCacheValidationDecision Fresh(
                string sourceKey,
                FileInfo source,
                string sourcePath,
                string cachePath)
            {
                return new TextureCacheValidationDecision(
                    sourceKey,
                    source,
                    sourcePath,
                    null,
                    cachePath,
                    TextureCacheValidationKind.Fresh);
            }

            internal static TextureCacheValidationDecision Reused(
                string sourceKey,
                FileInfo source,
                string sourcePath,
                string sourceHash,
                string cachePath)
            {
                return new TextureCacheValidationDecision(
                    sourceKey,
                    source,
                    sourcePath,
                    sourceHash,
                    cachePath,
                    TextureCacheValidationKind.Reused);
            }

            internal static TextureCacheValidationDecision Existing(
                TextureCacheEntry entry)
            {
                return FromEntry(entry, TextureCacheValidationKind.Existing);
            }

            internal static TextureCacheValidationDecision Missing(
                TextureCacheEntry entry)
            {
                return FromEntry(entry, TextureCacheValidationKind.Missing);
            }

            internal TextureCacheValidationDecision AsOriginal()
            {
                return FromEntry(Entry, TextureCacheValidationKind.Original);
            }

            private static TextureCacheValidationDecision FromEntry(
                TextureCacheEntry entry,
                TextureCacheValidationKind kind)
            {
                return new TextureCacheValidationDecision(
                    entry.Key,
                    entry.Source,
                    entry.SourcePath,
                    entry.SourceHash,
                    entry.FinalPath,
                    kind,
                    entry);
            }

            internal static TextureCacheValidationDecision Excluded(
                string sourceKey,
                FileInfo source,
                string sourcePath)
            {
                return new TextureCacheValidationDecision(
                    sourceKey,
                    source,
                    sourcePath,
                    null,
                    null,
                    TextureCacheValidationKind.Excluded);
            }

            internal static TextureCacheValidationDecision Unsupported(
                string sourceKey,
                FileInfo source,
                string sourcePath)
            {
                return new TextureCacheValidationDecision(
                    sourceKey,
                    source,
                    sourcePath,
                    null,
                    null,
                    TextureCacheValidationKind.Unsupported);
            }
        }

        private sealed class TextureCacheBuildBudget
        {
            private readonly long availableFreeBytes;
            private readonly long maximumCacheBytes;
            private readonly long minimumFreeBytes;
            private readonly bool builderAvailable;

            private long projectedCacheBytes;
            private long projectedTemporaryBytes;

            internal TextureCacheBuildBudget(
                long currentCacheBytes,
                long availableFreeBytes,
                long maximumCacheBytes,
                long minimumFreeBytes,
                bool builderAvailable)
            {
                projectedCacheBytes = currentCacheBytes;
                this.availableFreeBytes = availableFreeBytes;
                this.maximumCacheBytes = maximumCacheBytes;
                this.minimumFreeBytes = minimumFreeBytes;
                this.builderAvailable = builderAvailable;
            }

            internal IReadOnlyList<TextureCacheEntry> Select(
                IReadOnlyList<TextureCacheEntry> entries)
            {
                if (!builderAvailable || entries.Count == 0)
                {
                    return Array.Empty<TextureCacheEntry>();
                }

                List<TextureCacheEntry> selected =
                    new List<TextureCacheEntry>(entries.Count);
                foreach (TextureCacheEntry entry in entries)
                {
                    long temporaryBytes = entry.Source.Length + entry.EstimatedCacheBytes;
                    if ((maximumCacheBytes > 0L &&
                         projectedCacheBytes + entry.EstimatedCacheBytes >
                         maximumCacheBytes) ||
                        availableFreeBytes - projectedTemporaryBytes - temporaryBytes <
                        minimumFreeBytes)
                    {
                        continue;
                    }

                    selected.Add(entry);
                    projectedCacheBytes += entry.EstimatedCacheBytes;
                    projectedTemporaryBytes += temporaryBytes;
                }

                return selected;
            }
        }

    }
}
