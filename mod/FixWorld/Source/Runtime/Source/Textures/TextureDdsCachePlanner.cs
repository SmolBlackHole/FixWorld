using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace FixWorld.Textures
{
    internal sealed class TextureDdsCachePlanner
    {
        private readonly object sync = new object();
        private readonly string cacheRoot;
        private readonly TextureCacheStore store;
        private readonly TextureDdsCacheBuilder builder;
        private readonly TextureDdsCacheMetrics metrics;
        private readonly long maxCacheBytes;
        private readonly long minimumFreeBytes;
        private readonly Dictionary<string, IReadOnlyList<TextureCacheEntry>>
            deferredBuildsByPackage =
                new Dictionary<string, IReadOnlyList<TextureCacheEntry>>(
                    StringComparer.Ordinal);

        internal TextureDdsCachePlanner(
            string cacheRoot,
            TextureCacheStore store,
            TextureDdsCacheBuilder builder,
            TextureDdsCacheMetrics metrics,
            long maxCacheBytes,
            long minimumFreeBytes)
        {
            this.cacheRoot = cacheRoot;
            this.store = store;
            this.builder = builder;
            this.metrics = metrics;
            this.maxCacheBytes = maxCacheBytes;
            this.minimumFreeBytes = minimumFreeBytes;
        }

        internal void Apply(
            ModContentPack mod,
            Dictionary<string, FileInfo> files)
        {
            lock (sync)
            {
                try
                {
                    PrepareAndApplyFallback(mod, files);
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[FixWorld] DDS cache skipped for " + mod.PackageId +
                        ": " + exception);
                }
            }
        }

        internal IReadOnlyList<TextureCacheEntry> Complete()
        {
            lock (sync)
            {
                try
                {
                    PrepareCacheMaintenance();
                    return TakeDeferredBuildEntries();
                }
                finally
                {
                    metrics.SetCacheBytes(store.CurrentBytes);
                }
            }
        }

        internal void Shutdown()
        {
            lock (sync)
            {
                deferredBuildsByPackage.Clear();
            }
        }

        private TextureLoadTarget CreateTarget(ModContentPack mod)
        {
            string root = Path.GetFullPath(mod.RootDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            return new TextureLoadTarget(
                TextureCacheIdentity.Normalize(mod.PackageId),
                mod.Name,
                root,
                mod.foldersToLoadDescendingOrder.ToArray(),
                GenFilePaths.ContentPath<Texture2D>(),
                builder.Identity);
        }

        private TextureLoadPlan CreateLoadPlan(
            TextureLoadTarget target,
            TextureCacheSnapshot cacheSnapshot,
            Dictionary<string, FileInfo> discoveredFiles)
        {
            Dictionary<string, FileInfo> files = discoveredFiles ??
                ModFileLoader.DiscoverTextures(target.Folders, target.ContentPath);
            HashSet<string> shippedDdsPaths = new HashSet<string>(
                files.Keys
                    .Select(TextureCacheIdentity.Normalize)
                    .Where(path => path.EndsWith(".dds", StringComparison.Ordinal)),
                StringComparer.Ordinal);
            List<TextureLoadDecision> decisions = new List<TextureLoadDecision>();
            List<TextureCacheEntry> deferredBuilds = new List<TextureCacheEntry>();
            List<KeyValuePair<string, FileInfo>> sources = files.ToList();

            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                KeyValuePair<string, FileInfo> item = sources[sourceIndex];
                FileInfo source = item.Value;
                string sourceKey = TextureCacheIdentity.Normalize(item.Key);
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

                string sourcePath = TextureCacheIdentity.GetRelativeSourcePath(
                    source,
                    target.ModRoot);
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

                string sourceHash = TextureCacheIdentity.GetFileHash(
                    source.FullName);
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

        private TextureCacheEntry CreateBuildEntry(
            TextureLoadTarget target,
            string key,
            FileInfo source,
            string sourcePath,
            string sourceHash,
            int mipCount,
            long estimatedBytes)
        {
            string cacheKey = TextureCacheIdentity.GetContentKey(
                sourcePath,
                sourceHash,
                target.ConverterIdentity);
            string finalDirectory = Path.Combine(
                cacheRoot,
                TextureCacheIdentity.SanitizePathSegment(target.PackageId),
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

        private void PrepareAndApplyFallback(
            ModContentPack mod,
            Dictionary<string, FileInfo> files)
        {
            TextureLoadPlan plan = CreateLoadPlan(
                CreateTarget(mod),
                store.CreateValidationSnapshot(),
                files);
            plan.UseCachedFiles();
            deferredBuildsByPackage[plan.PackageId] = plan.DeferredBuilds;
            CommitLoadPlan(plan);
            metrics.FallbackMod();
        }

        private void CommitLoadPlan(TextureLoadPlan plan)
        {
            HashSet<string> retainedSourcePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextureLoadDecision decision in plan.Decisions)
            {
                switch (decision.Kind)
                {
                    case TextureLoadDecisionKind.Cached:
                        if (decision.Register)
                        {
                            store.RegisterExisting(
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
                            store.TouchPrepared(plan.PackageId, decision.SourcePath);
                        }

                        retainedSourcePaths.Add(decision.SourcePath);
                        metrics.Hit();
                        break;
                    case TextureLoadDecisionKind.Missing:
                        metrics.Miss();
                        break;
                    case TextureLoadDecisionKind.Excluded:
                        metrics.Exclude();
                        break;
                    case TextureLoadDecisionKind.Unsupported:
                        metrics.Unsupported();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            metrics.AddInvalidated(store.RemoveMissingSources(
                plan.PackageId,
                retainedSourcePaths));
            metrics.SetCacheBytes(store.CurrentBytes);
        }

        private void PrepareCacheMaintenance()
        {
            HashSet<string> activePackageIds = new HashSet<string>(
                    LoadedModManager.RunningModsListForReading
                        .Select(mod =>
                            TextureCacheIdentity.Normalize(mod.PackageId)),
                    StringComparer.Ordinal);
            int removedEntries = store.RemoveInactivePackages(activePackageIds);
            int budgetRemovals = store.EnforceBudget(maxCacheBytes);
            metrics.AddInvalidated(removedEntries + budgetRemovals);
            metrics.SetCacheBytes(store.CurrentBytes);
        }

        private IReadOnlyList<TextureCacheEntry> TakeDeferredBuildEntries()
        {
            TextureCacheEntry[] candidates = deferredBuildsByPackage.Values
                .SelectMany(entries => entries)
                .ToArray();
            deferredBuildsByPackage.Clear();
            TextureCacheBuildBudget budget = new TextureCacheBuildBudget(
                store.CurrentBytes,
                new DriveInfo(Path.GetPathRoot(cacheRoot)).AvailableFreeSpace,
                maxCacheBytes,
                minimumFreeBytes,
                builder.Available);
            IReadOnlyList<TextureCacheEntry> selected = budget.Select(candidates);
            metrics.AddBudgetSkipped(candidates.Length - selected.Count);
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
