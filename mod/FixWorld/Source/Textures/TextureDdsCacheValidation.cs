using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FixWorld.Loading;
using UnityEngine;
using Verse;

namespace FixWorld.Textures
{
    internal static partial class TextureDdsCache
    {
        private static readonly Dictionary<string, TextureLoadPlan> PreparedPlans =
            new Dictionary<string, TextureLoadPlan>(StringComparer.Ordinal);
        private static readonly Dictionary<string, IReadOnlyList<TextureCacheEntry>>
            DeferredBuildsByPackage =
                new Dictionary<string, IReadOnlyList<TextureCacheEntry>>(
                    StringComparer.Ordinal);

        internal static bool TryCreateValidationPlan(
            IReadOnlyList<ModContentPack> mods,
            out LoadingActionPlan plan)
        {
            plan = default;
            if (!enabled ||
                workerCount <= 0 ||
                cacheStore == null ||
                !Prefs.TextureCompression ||
                mods == null ||
                mods.Count == 0)
            {
                return false;
            }

            TextureCacheSnapshot cacheSnapshot;
            TextureLoadTarget[] targets;
            lock (Sync)
            {
                cacheSnapshot = cacheStore.CreateValidationSnapshot();
                targets = mods.Select(CreateTarget).ToArray();
            }

            LoadingWorkItem[] tasks = new LoadingWorkItem[targets.Length];
            for (int index = 0; index < targets.Length; index++)
            {
                TextureLoadTarget target = targets[index];
                int current = index + 1;
                tasks[index] = LoadingWorkItem.CreateParallelThenCommit(
                    LoadingStage.Content,
                    LoadingStep.ValidateTextureCache,
                    "Prepare texture cache",
                    "Discovering and validating textures for " + target.ModName,
                    target.ModName,
                    LoadingModAttribution.Exact(target.PackageId, target.ModName),
                    current,
                    targets.Length,
                    continueOnFailure: true,
                    prepare: () => CreateLoadPlan(target, cacheSnapshot, null),
                    commit: StorePreparedPlan);
            }

            plan = new LoadingActionPlan(
                "FixWorld texture discovery and DDS validation",
                LoadingModAttribution.Global,
                new LoadingPipelineStage(
                    0,
                    "Prepare texture cache",
                    LoadingStage.Content,
                    LoadingStep.ValidateTextureCache,
                    LoadingExecutionMode.ParallelThenCommit,
                    tasks,
                    maxParallelism: Math.Min(workerCount, 4)));
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
                if (!PreparedPlans.TryGetValue(
                        Normalize(mod.PackageId),
                        out TextureLoadPlan prepared))
                {
                    return false;
                }

                files = prepared.Files;
                return true;
            }
        }

        private static TextureLoadTarget CreateTarget(ModContentPack mod)
        {
            string root = Path.GetFullPath(mod.RootDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            return new TextureLoadTarget(
                Normalize(mod.PackageId),
                mod.Name,
                root,
                mod.foldersToLoadDescendingOrder.ToArray(),
                GenFilePaths.ContentPath<Texture2D>(),
                builder.Identity);
        }

        private static TextureLoadPlan CreateLoadPlan(
            TextureLoadTarget target,
            TextureCacheSnapshot cacheSnapshot,
            Dictionary<string, FileInfo> discoveredFiles)
        {
            LoadingOperation operation = LoadingEvents.Begin(
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
                Dictionary<string, FileInfo> files = discoveredFiles ??
                    ModFileLoader.DiscoverTextures(target.Folders, target.ContentPath);
                HashSet<string> shippedDdsPaths = new HashSet<string>(
                    files.Keys
                        .Select(Normalize)
                        .Where(path => path.EndsWith(".dds", StringComparison.Ordinal)),
                    StringComparer.Ordinal);
                List<TextureLoadDecision> decisions = new List<TextureLoadDecision>();
                List<TextureCacheEntry> deferredBuilds = new List<TextureCacheEntry>();
                List<KeyValuePair<string, FileInfo>> sources = files.ToList();

                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    KeyValuePair<string, FileInfo> item = sources[sourceIndex];
                    operation.ReportProgress(
                        sourceIndex + 1,
                        sources.Count,
                        "Checking cached textures for " + target.ModName);
                    FileInfo source = item.Value;
                    string sourceKey = Normalize(item.Key);
                    string extension = source.Extension.ToLowerInvariant();
                    if (extension == ".dds" ||
                        shippedDdsPaths.Contains(Path.ChangeExtension(sourceKey, ".dds")) ||
                        !source.FullName.StartsWith(
                            target.ModRoot,
                            StringComparison.OrdinalIgnoreCase) ||
                        extension != ".png" && extension != ".jpg" &&
                        extension != ".jpeg")
                    {
                        continue;
                    }

                    string sourcePath = GetRelativeSourcePath(source, target.ModRoot);
                    if (cacheSnapshot.TryGetFresh(
                            target.PackageId,
                            sourcePath,
                            source,
                            target.ConverterIdentity,
                            out string cachePath))
                    {
                        decisions.Add(TextureLoadDecision.Cached(
                            item.Key,
                            source,
                            sourcePath,
                            cachePath,
                            register: false));
                        continue;
                    }

                    if (!TextureDimensions.TryRead(source, out TextureDimensions dimensions))
                    {
                        decisions.Add(TextureLoadDecision.Original(
                            sourcePath,
                            TextureLoadDecisionKind.Unsupported));
                        continue;
                    }

                    int mipCount = dimensions.GetBc3MipCount();
                    if (mipCount == 0)
                    {
                        decisions.Add(TextureLoadDecision.Original(
                            sourcePath,
                            TextureLoadDecisionKind.Excluded));
                        continue;
                    }

                    string sourceHash = GetFileHash(source.FullName);
                    if (cacheSnapshot.TryGetReusable(
                            target.PackageId,
                            sourcePath,
                            sourceHash,
                            target.ConverterIdentity,
                            out string reusablePath))
                    {
                        decisions.Add(TextureLoadDecision.Cached(
                            item.Key,
                            source,
                            sourcePath,
                            reusablePath,
                            register: true,
                            sourceHash));
                        continue;
                    }

                    TextureCacheEntry build = CreateBuildEntry(
                        target,
                        item.Key,
                        source,
                        sourcePath,
                        sourceHash,
                        mipCount,
                        dimensions.GetBc3FileSize(mipCount));
                    if (File.Exists(build.FinalPath))
                    {
                        decisions.Add(TextureLoadDecision.Cached(
                            item.Key,
                            source,
                            sourcePath,
                            build.FinalPath,
                            register: true,
                            sourceHash));
                        continue;
                    }

                    decisions.Add(TextureLoadDecision.Original(
                        sourcePath,
                        TextureLoadDecisionKind.Missing));
                    deferredBuilds.Add(build);
                }

                return new TextureLoadPlan(
                    target.PackageId,
                    files,
                    decisions,
                    deferredBuilds);
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

        private static TextureCacheEntry CreateBuildEntry(
            TextureLoadTarget target,
            string key,
            FileInfo source,
            string sourcePath,
            string sourceHash,
            int mipCount,
            long estimatedBytes)
        {
            string cacheKey = GetContentCacheKey(
                sourcePath,
                sourceHash,
                target.ConverterIdentity);
            string finalDirectory = Path.Combine(
                cacheRoot,
                SanitizePathSegment(target.PackageId),
                cacheKey);
            return new TextureCacheEntry(
                key,
                target.PackageId,
                sourcePath,
                sourceHash,
                target.ConverterIdentity,
                source,
                cacheKey,
                mipCount,
                estimatedBytes,
                finalDirectory,
                Path.Combine(
                    finalDirectory,
                    Path.GetFileNameWithoutExtension(source.Name) + ".dds"));
        }

        private static void StorePreparedPlan(TextureLoadPlan plan)
        {
            lock (Sync)
            {
                plan.UseCachedFiles();
                PreparedPlans[plan.PackageId] = plan;
                DeferredBuildsByPackage[plan.PackageId] = plan.DeferredBuilds;
                workerPreparedMods++;
            }
        }

        private static bool TryApplyPrepared(
            ModContentPack mod,
            Dictionary<string, FileInfo> files)
        {
            string packageId = Normalize(mod.PackageId);
            if (!PreparedPlans.TryGetValue(packageId, out TextureLoadPlan prepared))
            {
                return false;
            }

            PreparedPlans.Remove(packageId);
            if (!ReferenceEquals(files, prepared.Files))
            {
                return false;
            }

            CommitLoadPlan(prepared);
            workerAppliedMods++;
            return true;
        }

        private static void PrepareAndApplyFallback(
            ModContentPack mod,
            Dictionary<string, FileInfo> files)
        {
            TextureLoadPlan plan = CreateLoadPlan(
                CreateTarget(mod),
                cacheStore.CreateValidationSnapshot(),
                files);
            plan.UseCachedFiles();
            DeferredBuildsByPackage[plan.PackageId] = plan.DeferredBuilds;
            CommitLoadPlan(plan);
            workerFallbackMods++;
        }

        private static bool HasPreparedPlan(ModContentPack mod)
        {
            return PreparedPlans.ContainsKey(Normalize(mod.PackageId));
        }

        private static void CommitLoadPlan(TextureLoadPlan plan)
        {
            HashSet<string> retainedSourcePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextureLoadDecision decision in plan.Decisions)
            {
                switch (decision.Kind)
                {
                    case TextureLoadDecisionKind.Cached:
                        if (decision.Register)
                        {
                            cacheStore.RegisterExisting(
                                plan.PackageId,
                                decision.SourcePath,
                                decision.Source,
                                decision.SourceHash,
                                builder.Identity,
                                decision.CachePath,
                                createdAfterOpen: false);
                        }
                        else
                        {
                            cacheStore.TouchPrepared(plan.PackageId, decision.SourcePath);
                        }

                        retainedSourcePaths.Add(decision.SourcePath);
                        hitCount++;
                        break;
                    case TextureLoadDecisionKind.Missing:
                        missCount++;
                        break;
                    case TextureLoadDecisionKind.Excluded:
                        excludedCount++;
                        break;
                    case TextureLoadDecisionKind.Unsupported:
                        unsupportedCount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            invalidatedCount += cacheStore.RemoveMissingSources(
                plan.PackageId,
                retainedSourcePaths);
            currentCacheBytes = cacheStore.CurrentBytes;
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

        private static void ClearPreparedPlans()
        {
            PreparedPlans.Clear();
        }

        private static IReadOnlyList<TextureCacheEntry> TakeDeferredBuildEntries()
        {
            TextureCacheEntry[] candidates = DeferredBuildsByPackage.Values
                .SelectMany(entries => entries)
                .ToArray();
            DeferredBuildsByPackage.Clear();
            TextureCacheBuildBudget budget = new TextureCacheBuildBudget(
                cacheStore.CurrentBytes,
                new DriveInfo(Path.GetPathRoot(cacheRoot)).AvailableFreeSpace,
                maxCacheBytes,
                minimumFreeBytes,
                builder.Available);
            IReadOnlyList<TextureCacheEntry> selected = budget.Select(candidates);
            budgetSkippedCount += candidates.Length - selected.Count;
            return selected;
        }

        private sealed class TextureLoadTarget
        {
            internal readonly string PackageId;
            internal readonly string ModName;
            internal readonly string ModRoot;
            internal readonly string[] Folders;
            internal readonly string ContentPath;
            internal readonly string ConverterIdentity;

            internal TextureLoadTarget(
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

        private sealed class TextureLoadPlan
        {
            internal readonly string PackageId;
            internal readonly Dictionary<string, FileInfo> Files;
            internal readonly IReadOnlyList<TextureLoadDecision> Decisions;
            internal readonly IReadOnlyList<TextureCacheEntry> DeferredBuilds;

            internal TextureLoadPlan(
                string packageId,
                Dictionary<string, FileInfo> files,
                IReadOnlyList<TextureLoadDecision> decisions,
                IReadOnlyList<TextureCacheEntry> deferredBuilds)
            {
                PackageId = packageId;
                Files = files;
                Decisions = decisions;
                DeferredBuilds = deferredBuilds;
            }

            internal void UseCachedFiles()
            {
                foreach (TextureLoadDecision decision in Decisions)
                {
                    if (decision.Kind == TextureLoadDecisionKind.Cached)
                    {
                        Files[decision.SourceKey] = new FileInfo(decision.CachePath);
                    }
                }
            }
        }

        private enum TextureLoadDecisionKind
        {
            Cached,
            Missing,
            Excluded,
            Unsupported
        }

        private readonly struct TextureLoadDecision
        {
            internal readonly string SourceKey;
            internal readonly FileInfo Source;
            internal readonly string SourcePath;
            internal readonly string SourceHash;
            internal readonly string CachePath;
            internal readonly TextureLoadDecisionKind Kind;
            internal readonly bool Register;

            private TextureLoadDecision(
                string sourceKey,
                FileInfo source,
                string sourcePath,
                string sourceHash,
                string cachePath,
                TextureLoadDecisionKind kind,
                bool register)
            {
                SourceKey = sourceKey;
                Source = source;
                SourcePath = sourcePath;
                SourceHash = sourceHash;
                CachePath = cachePath;
                Kind = kind;
                Register = register;
            }

            internal static TextureLoadDecision Cached(
                string sourceKey,
                FileInfo source,
                string sourcePath,
                string cachePath,
                bool register,
                string sourceHash = null)
            {
                return new TextureLoadDecision(
                    sourceKey,
                    source,
                    sourcePath,
                    sourceHash,
                    cachePath,
                    TextureLoadDecisionKind.Cached,
                    register);
            }

            internal static TextureLoadDecision Original(
                string sourcePath,
                TextureLoadDecisionKind kind)
            {
                return new TextureLoadDecision(
                    null,
                    null,
                    sourcePath,
                    null,
                    null,
                    kind,
                    register: false);
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
