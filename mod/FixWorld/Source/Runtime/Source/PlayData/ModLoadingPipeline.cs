using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using FixWorld.Content;
using FixWorld.Textures;
using Verse;

namespace FixWorld.PlayData
{
    internal sealed class ModLoadingPipeline
    {
        private const string FixWorldPackageId = "smolblackhole.fixworld";
        private const float DefaultDdsCacheMaxGiB = 6.0f;

        private readonly ModFileIndex files;
        private readonly TextureDdsCache textures;

        internal ModLoadingPipeline(
            ModFileIndex files,
            TextureDdsCache textures)
        {
            this.files = files ?? throw new ArgumentNullException(nameof(files));
            this.textures = textures ??
                throw new ArgumentNullException(nameof(textures));
        }

        internal void Reset()
        {
            files.Clear();
            textures.BeginIndex();
            Profile("XmlInheritance.Clear()", XmlInheritance.Clear);
        }

        internal void InitializeMods()
        {
            Profile("InitializeMods()", LoadedModManager.InitializeMods);
        }

        internal void PrepareContent()
        {
            Profile(
                "LoadModContent()",
                () => LoadedModManager.LoadModContent(hotReload: false));
        }

        internal void CreateModClasses()
        {
            Profile("CreateModClasses()", LoadedModManager.CreateModClasses);
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
            textures.Prepare(files);
        }

        internal ModXmlState LoadAndPatchXml()
        {
            List<LoadableXmlAsset> assets = Profile(
                "LoadModXML()",
                () => LoadedModManager.LoadModXML(hotReload: false));
            Dictionary<XmlNode, LoadableXmlAsset> lookup =
                new Dictionary<XmlNode, LoadableXmlAsset>();
            XmlDocument document = Profile(
                "CombineIntoUnifiedXML()",
                () => LoadedModManager.CombineIntoUnifiedXML(assets, lookup));

            TKeySystem.Clear();
            Profile("TKeySystem.Parse()", () => TKeySystem.Parse(document));
            Profile("ErrorCheckPatches()", LoadedModManager.ErrorCheckPatches);
            Profile(
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
                "ParseAndProcessXML()",
                () => LoadedModManager.ParseAndProcessXML(
                    state.Document,
                    state.AssetLookup,
                    hotReload: false));
            Profile("ClearCachedPatches()", LoadedModManager.ClearCachedPatches);
            Profile("XmlInheritance.Clear()", XmlInheritance.Clear);
        }

        private static void Profile(string label, Action action)
        {
            DeepProfiler.Start(label);
            try
            {
                action();
            }
            finally
            {
                DeepProfiler.End();
            }
        }

        private static TResult Profile<TResult>(
            string label,
            Func<TResult> action)
        {
            DeepProfiler.Start(label);
            try
            {
                return action();
            }
            finally
            {
                DeepProfiler.End();
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
