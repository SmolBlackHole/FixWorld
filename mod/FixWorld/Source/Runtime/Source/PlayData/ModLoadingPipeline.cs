using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using FixWorld.Content;
using FixWorld.Events;
using FixWorld.Textures;
using Verse;

namespace FixWorld.PlayData
{
    internal sealed class ModLoadingPipeline
    {
        private const string FixWorldPackageId = "smolblackhole.fixworld";
        private const float DefaultDdsCacheMaxGiB = 6.0f;

        private readonly EventBus events;
        private readonly CombinedXmlCache combinedXml;
        private readonly ModFileIndex files;
        private readonly TextureDdsCache textures;

        internal ModLoadingPipeline(
            EventBus events,
            CombinedXmlCache combinedXml,
            ModFileIndex files,
            TextureDdsCache textures)
        {
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            this.combinedXml = combinedXml ??
                throw new ArgumentNullException(nameof(combinedXml));
            this.files = files ?? throw new ArgumentNullException(nameof(files));
            this.textures = textures ??
                throw new ArgumentNullException(nameof(textures));
        }

        internal void Reset()
        {
            textures.BeginIndex();
            Profile(
                PlayDataLoadStage.Reset,
                "XmlInheritance.Clear()",
                XmlInheritance.Clear);
        }

        internal void InitializeMods()
        {
            Profile(
                PlayDataLoadStage.InitializeMods,
                "InitializeMods()",
                LoadedModManager.InitializeMods);
        }

        internal void PrepareContent()
        {
            Profile(
                PlayDataLoadStage.PrepareModContent,
                "LoadModContent()",
                () => LoadedModManager.LoadModContent(hotReload: false));
        }

        internal void CreateModClasses()
        {
            Profile(
                PlayDataLoadStage.CreateModClasses,
                "CreateModClasses()",
                LoadedModManager.CreateModClasses);
        }

        internal void IndexContent()
        {
            ModContentPack fixWorld = LoadedModManager
                .RunningModsListForReading
                .FirstOrDefault(mod => string.Equals(
                    mod.PackageId,
                    FixWorldPackageId,
                    StringComparison.OrdinalIgnoreCase));
            if (fixWorld != null)
            {
                textures.Attach(
                    fixWorld.RootDir,
                    DefaultDdsCacheMaxGiB);
            }

            files.Rebuild(LoadedModManager.RunningModsListForReading);
        }

        internal void PrepareTextureCache()
        {
            textures.Prepare(files);
        }

        internal ModXmlState LoadAndPatchXml()
        {
            ModXmlState state = combinedXml.Enabled
                ? LoadCombinedXml()
                : LoadCombinedXmlFromMods(out _);
            XmlDocument document = state.Document;
            Dictionary<XmlNode, LoadableXmlAsset> lookup = state.AssetLookup;

            TKeySystem.Clear();
            Profile(
                PlayDataLoadStage.LoadAndPatchXml,
                "TKeySystem.Parse()",
                () => TKeySystem.Parse(document));
            Profile(
                PlayDataLoadStage.LoadAndPatchXml,
                "ErrorCheckPatches()",
                LoadedModManager.ErrorCheckPatches);
            Profile(
                PlayDataLoadStage.LoadAndPatchXml,
                "ApplyPatches()",
                () => LoadedModManager.ApplyPatches(document, lookup));
            return state;
        }

        private ModXmlState LoadCombinedXml()
        {
            TryCacheOperation(
                "ProbeCombinedXmlCache()",
                combinedXml.Probe,
                out CombinedXmlProbe probe);

            ModXmlState restored = null;
            double preloadMilliseconds = 0.0;
            if (TryCacheOperation(
                    "AcceptPreloadedCombinedXML()",
                    () => combinedXml.TryRestore(
                        probe,
                        out restored,
                        out preloadMilliseconds),
                    out bool hit) &&
                hit)
            {
                Log.Message(
                    "[FixWorld] Reused pre-parsed combined XML cache; " +
                    "preloader parse=" +
                    preloadMilliseconds.ToString("F1") + " ms.");
                return restored;
            }

            ModXmlState state = LoadCombinedXmlFromMods(out List<LoadableXmlAsset> assets);
            if (TryCacheOperation(
                    "VerifyCombinedXmlCacheInputs()",
                    combinedXml.Probe,
                    out CombinedXmlProbe completedProbe) &&
                probe != null &&
                completedProbe != null &&
                string.Equals(
                    probe.Identity,
                    completedProbe.Identity,
                    StringComparison.Ordinal))
            {
                TryCacheOperation(
                    "StoreCombinedXmlCache()",
                    () => combinedXml.Store(completedProbe, assets, state));
            }

            return state;
        }

        private bool TryCacheOperation<TResult>(
            string label,
            Func<TResult> operation,
            out TResult result)
        {
            try
            {
                result = Profile(
                    PlayDataLoadStage.LoadAndPatchXml,
                    label,
                    operation);
                return true;
            }
            catch (Exception exception)
            {
                result = default(TResult);
                Log.Warning(
                    "[FixWorld] Combined XML cache operation failed (" + label +
                    "): " + exception);
                return false;
            }
        }

        private void TryCacheOperation(string label, Action operation)
        {
            TryCacheOperation(
                label,
                () =>
                {
                    operation();
                    return true;
                },
                out _);
        }

        private ModXmlState LoadCombinedXmlFromMods(
            out List<LoadableXmlAsset> assets)
        {
            List<LoadableXmlAsset> loadedAssets = Profile(
                PlayDataLoadStage.LoadAndPatchXml,
                "LoadModXML()",
                () => LoadedModManager.LoadModXML(hotReload: false));
            assets = loadedAssets;
            Dictionary<XmlNode, LoadableXmlAsset> lookup =
                new Dictionary<XmlNode, LoadableXmlAsset>();
            XmlDocument document = Profile(
                PlayDataLoadStage.LoadAndPatchXml,
                "CombineIntoUnifiedXML()",
                () => LoadedModManager.CombineIntoUnifiedXML(
                    loadedAssets,
                    lookup));
            return new ModXmlState(document, lookup);
        }

        internal void ImportDefinitions(ModXmlState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            Profile(
                PlayDataLoadStage.ImportDefinitions,
                "ParseAndProcessXML()",
                () => LoadedModManager.ParseAndProcessXML(
                    state.Document,
                    state.AssetLookup,
                    hotReload: false));
            Profile(
                PlayDataLoadStage.ImportDefinitions,
                "ClearCachedPatches()",
                LoadedModManager.ClearCachedPatches);
            Profile(
                PlayDataLoadStage.ImportDefinitions,
                "XmlInheritance.Clear()",
                XmlInheritance.Clear);
        }

        private void Profile(
            PlayDataLoadStage stage,
            string label,
            Action action)
        {
            DeepProfiler.Start(label);
            Stopwatch stopwatch = Stopwatch.StartNew();
            bool succeeded = false;
            try
            {
                action();
                succeeded = true;
            }
            finally
            {
                stopwatch.Stop();
                DeepProfiler.End();
                events.Publish(new PlayDataOperationEvent(
                    stage,
                    label,
                    stopwatch.Elapsed,
                    succeeded));
            }
        }

        private TResult Profile<TResult>(
            PlayDataLoadStage stage,
            string label,
            Func<TResult> action)
        {
            DeepProfiler.Start(label);
            Stopwatch stopwatch = Stopwatch.StartNew();
            bool succeeded = false;
            try
            {
                TResult result = action();
                succeeded = true;
                return result;
            }
            finally
            {
                stopwatch.Stop();
                DeepProfiler.End();
                events.Publish(new PlayDataOperationEvent(
                    stage,
                    label,
                    stopwatch.Elapsed,
                    succeeded));
            }
        }
    }

    internal sealed class ModXmlState
    {
        internal ModXmlState(
            XmlDocument document,
            Dictionary<XmlNode, LoadableXmlAsset> assetLookup)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            AssetLookup = assetLookup ??
                throw new ArgumentNullException(nameof(assetLookup));
        }

        internal XmlDocument Document { get; }

        internal Dictionary<XmlNode, LoadableXmlAsset> AssetLookup { get; }
    }
}
