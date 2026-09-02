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
        private readonly ModFileIndex files;
        private readonly TextureDdsCache textures;

        internal ModLoadingPipeline(
            EventBus events,
            ModFileIndex files,
            TextureDdsCache textures)
        {
            this.events = events ?? throw new ArgumentNullException(nameof(events));
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
            List<LoadableXmlAsset> assets = Profile(
                PlayDataLoadStage.LoadAndPatchXml,
                "LoadModXML()",
                () => LoadedModManager.LoadModXML(hotReload: false));
            Dictionary<XmlNode, LoadableXmlAsset> lookup =
                new Dictionary<XmlNode, LoadableXmlAsset>();
            XmlDocument document = Profile(
                PlayDataLoadStage.LoadAndPatchXml,
                "CombineIntoUnifiedXML()",
                () => LoadedModManager.CombineIntoUnifiedXML(assets, lookup));

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
