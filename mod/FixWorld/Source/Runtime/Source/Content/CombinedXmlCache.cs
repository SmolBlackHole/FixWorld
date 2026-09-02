using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using FixWorld.PlayData;
using FixWorld.Runtime;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FixWorld.Content
{
    internal sealed class CombinedXmlCache
    {
        private static readonly FieldInfo AssetNameField =
            typeof(LoadableXmlAsset).GetField(nameof(LoadableXmlAsset.name));
        private static readonly FieldInfo AssetFolderField =
            typeof(LoadableXmlAsset).GetField(
                nameof(LoadableXmlAsset.fullFolderPath));
        private static readonly FieldInfo AssetModField =
            typeof(LoadableXmlAsset).GetField(nameof(LoadableXmlAsset.mod));
        private static readonly MethodBase[] ReplacedMethods =
        {
            AccessTools.Method(
                typeof(LoadedModManager),
                nameof(LoadedModManager.LoadModXML),
                new[] { typeof(bool) }),
            AccessTools.Method(
                typeof(LoadedModManager),
                nameof(LoadedModManager.CombineIntoUnifiedXML),
                new[]
                {
                    typeof(List<LoadableXmlAsset>),
                    typeof(Dictionary<XmlNode, LoadableXmlAsset>)
                }),
            AccessTools.Method(
                typeof(ModContentPack),
                nameof(ModContentPack.LoadDefs),
                new[] { typeof(bool) }),
            AccessTools.Method(
                typeof(DirectXmlLoader),
                nameof(DirectXmlLoader.XmlAssetsInModFolder),
                new[]
                {
                    typeof(ModContentPack),
                    typeof(string),
                    typeof(List<string>)
                }),
            AccessTools.Constructor(
                typeof(LoadableXmlAsset),
                new[] { typeof(FileInfo), typeof(ModContentPack) })
        };

        private bool reportedPatchedPipeline;

        internal bool Enabled => CombinedXmlCacheContract.Enabled;

        internal void DiscardPublished()
        {
            CombinedXmlCacheContract.TakePublished();
        }

        internal CombinedXmlProbe Probe()
        {
            if (!CanReplaceVanillaXmlPath(out MethodBase patchedMethod))
            {
                if (!reportedPatchedPipeline)
                {
                    reportedPatchedPipeline = true;
                    Log.Message(
                        "[FixWorld] Combined XML cache disabled because " +
                        patchedMethod.DeclaringType?.FullName + "." +
                        patchedMethod.Name + " has Harmony patches.");
                }

                return null;
            }

            IReadOnlyList<ModContentPack> mods =
                LoadedModManager.RunningModsListForReading;
            StringBuilder identity = new StringBuilder();
            Dictionary<string, int> sources = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

            Append(identity, VersionControl.CurrentVersionStringWithRev);
            Append(identity, mods.Count.ToString(CultureInfo.InvariantCulture));
            for (int modIndex = 0; modIndex < mods.Count; modIndex++)
            {
                ModContentPack mod = mods[modIndex];
                Append(identity, mod.PackageId ?? string.Empty);
                Append(identity, NormalizePath(mod.RootDir));
                Append(
                    identity,
                    mod.foldersToLoadDescendingOrder.Count.ToString(
                        CultureInfo.InvariantCulture));
                foreach (string folder in mod.foldersToLoadDescendingOrder)
                {
                    Append(identity, NormalizePath(folder));
                }

                List<EffectiveXmlFile> files = FindEffectiveDefFiles(mod);
                files.Sort((left, right) => string.Compare(
                    left.RelativePath,
                    right.RelativePath,
                    StringComparison.Ordinal));
                Append(identity, files.Count.ToString(CultureInfo.InvariantCulture));
                foreach (EffectiveXmlFile file in files)
                {
                    Append(identity, file.RelativePath);
                    Append(identity, NormalizePath(file.File.FullName));
                    Append(
                        identity,
                        file.File.Length.ToString(CultureInfo.InvariantCulture));
                    Append(
                        identity,
                        file.File.LastWriteTimeUtc.Ticks.ToString(
                            CultureInfo.InvariantCulture));
                    sources.Add(SourceKey(modIndex, file.File.FullName), modIndex);
                }
            }

            byte[] identityBytes = Encoding.UTF8.GetBytes(identity.ToString());
            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(identityBytes);
            }

            return new CombinedXmlProbe(
                BitConverter.ToString(hash).Replace("-", string.Empty),
                sources);
        }

        internal bool TryRestore(
            CombinedXmlProbe probe,
            out ModXmlState state,
            out double preloadMilliseconds)
        {
            state = null;
            preloadMilliseconds = 0.0;
            CombinedXmlArtifact artifact =
                CombinedXmlCacheContract.TakePublished();
            if (probe == null || artifact == null ||
                !string.Equals(
                    artifact.RimWorldVersion,
                    VersionControl.CurrentVersionStringWithRev,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    artifact.Identity,
                    probe.Identity,
                    StringComparison.Ordinal) ||
                !ValidateShape(artifact, probe))
            {
                return false;
            }

            IReadOnlyList<ModContentPack> mods =
                LoadedModManager.RunningModsListForReading;
            LoadableXmlAsset[] sources =
                new LoadableXmlAsset[artifact.SourceMods.Length];
            for (int index = 0; index < sources.Length; index++)
            {
                int modIndex = artifact.SourceMods[index];
                string fullPath = Path.Combine(
                    artifact.SourceFolders[index],
                    artifact.SourceNames[index]);
                if (modIndex < 0 || modIndex >= mods.Count ||
                    !probe.Sources.ContainsKey(SourceKey(modIndex, fullPath)))
                {
                    return false;
                }

                sources[index] = CreateAsset(
                    artifact.SourceNames[index],
                    artifact.SourceFolders[index],
                    mods[modIndex]);
            }

            Dictionary<XmlNode, LoadableXmlAsset> lookup =
                new Dictionary<XmlNode, LoadableXmlAsset>();
            XmlNodeList nodes = artifact.Document.DocumentElement.ChildNodes;
            int nodeIndex = 0;
            foreach (XmlNode node in nodes)
            {
                int sourceIndex = artifact.NodeSources[nodeIndex++];
                if (sourceIndex < 0 || sourceIndex >= sources.Length)
                {
                    return false;
                }

                lookup.Add(node, sources[sourceIndex]);
            }

            preloadMilliseconds = artifact.PreloadMilliseconds;
            state = new ModXmlState(artifact.Document, lookup);
            return true;
        }

        internal void Store(
            CombinedXmlProbe probe,
            IReadOnlyList<LoadableXmlAsset> assets,
            ModXmlState state)
        {
            if (probe == null || assets == null || state == null ||
                assets.Count != probe.Sources.Count)
            {
                return;
            }

            IReadOnlyList<ModContentPack> mods =
                LoadedModManager.RunningModsListForReading;
            Dictionary<ModContentPack, int> modIndices =
                new Dictionary<ModContentPack, int>();
            for (int index = 0; index < mods.Count; index++)
            {
                modIndices.Add(mods[index], index);
            }

            int[] sourceMods = new int[assets.Count];
            string[] sourceNames = new string[assets.Count];
            string[] sourceFolders = new string[assets.Count];
            Dictionary<LoadableXmlAsset, int> sourceIndices =
                new Dictionary<LoadableXmlAsset, int>();
            for (int index = 0; index < assets.Count; index++)
            {
                LoadableXmlAsset asset = assets[index];
                if (asset == null || asset.mod == null ||
                    !modIndices.TryGetValue(asset.mod, out int modIndex) ||
                    !probe.Sources.ContainsKey(
                        SourceKey(modIndex, asset.FullFilePath)))
                {
                    return;
                }

                sourceMods[index] = modIndex;
                sourceNames[index] = asset.name;
                sourceFolders[index] = asset.fullFolderPath;
                sourceIndices.Add(asset, index);
            }

            XmlNodeList nodes = state.Document.DocumentElement.ChildNodes;
            int[] nodeSources = new int[nodes.Count];
            int nodeIndex = 0;
            foreach (XmlNode node in nodes)
            {
                if (!state.AssetLookup.TryGetValue(
                        node,
                        out LoadableXmlAsset asset) ||
                    !sourceIndices.TryGetValue(
                        asset,
                        out nodeSources[nodeIndex]))
                {
                    return;
                }

                nodeIndex++;
            }

            string path = CombinedXmlCacheContract.GetPath(
                GenFilePaths.SaveDataFolderPath);
            AtomicFile.Write(
                path,
                stream => CombinedXmlCacheContract.Write(
                    stream,
                    VersionControl.CurrentVersionStringWithRev,
                    probe.Identity,
                    sourceMods,
                    sourceNames,
                    sourceFolders,
                    nodeSources,
                    state.Document));
        }

        private static bool ValidateShape(
            CombinedXmlArtifact artifact,
            CombinedXmlProbe probe)
        {
            return artifact.Document?.DocumentElement != null &&
                   string.Equals(
                       artifact.Document.DocumentElement.Name,
                       "Defs",
                       StringComparison.Ordinal) &&
                   artifact.SourceMods != null &&
                   artifact.SourceNames != null &&
                   artifact.SourceFolders != null &&
                   artifact.NodeSources != null &&
                   artifact.SourceMods.Length == probe.Sources.Count &&
                   artifact.SourceMods.Length == artifact.SourceNames.Length &&
                   artifact.SourceMods.Length == artifact.SourceFolders.Length &&
                   artifact.NodeSources.Length ==
                   artifact.Document.DocumentElement.ChildNodes.Count;
        }

        private static bool CanReplaceVanillaXmlPath(
            out MethodBase patchedMethod)
        {
            foreach (MethodBase method in ReplacedMethods)
            {
                if (method != null && Harmony.GetPatchInfo(method) != null)
                {
                    patchedMethod = method;
                    return false;
                }
            }

            patchedMethod = null;
            return true;
        }

        private static LoadableXmlAsset CreateAsset(
            string name,
            string folder,
            ModContentPack mod)
        {
            LoadableXmlAsset asset = (LoadableXmlAsset)
                FormatterServices.GetUninitializedObject(
                    typeof(LoadableXmlAsset));
            AssetNameField.SetValue(asset, name);
            AssetFolderField.SetValue(asset, folder);
            AssetModField.SetValue(asset, mod);
            return asset;
        }

        private static List<EffectiveXmlFile> FindEffectiveDefFiles(
            ModContentPack mod)
        {
            Dictionary<string, FileInfo> effective =
                new Dictionary<string, FileInfo>(StringComparer.Ordinal);
            foreach (string folder in mod.foldersToLoadDescendingOrder)
            {
                DirectoryInfo defs = new DirectoryInfo(
                    Path.Combine(folder, "Defs"));
                if (!defs.Exists)
                {
                    continue;
                }

                foreach (FileInfo file in defs.GetFiles(
                             "*.xml",
                             SearchOption.AllDirectories))
                {
                    if (file.Name.StartsWith("._", StringComparison.Ordinal) ||
                        file.Name.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string relativePath = file.FullName.Substring(
                        folder.Length + 1);
                    if (!effective.ContainsKey(relativePath))
                    {
                        effective.Add(relativePath, file);
                    }
                }
            }

            return effective
                .Select(pair => new EffectiveXmlFile(pair.Key, pair.Value))
                .ToList();
        }

        private static void Append(StringBuilder builder, string value)
        {
            builder.Append(value?.Length ?? 0);
            builder.Append(':');
            builder.Append(value);
            builder.Append('\n');
        }

        private static string SourceKey(int modIndex, string path)
        {
            return modIndex.ToString(CultureInfo.InvariantCulture) + "\n" +
                   NormalizePath(path);
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    internal sealed class CombinedXmlProbe
    {
        internal CombinedXmlProbe(
            string identity,
            Dictionary<string, int> sources)
        {
            Identity = identity;
            Sources = sources;
        }

        internal string Identity { get; }
        internal Dictionary<string, int> Sources { get; }
    }

    internal readonly struct EffectiveXmlFile
    {
        internal EffectiveXmlFile(string relativePath, FileInfo file)
        {
            RelativePath = relativePath;
            File = file;
        }

        internal string RelativePath { get; }
        internal FileInfo File { get; }
    }
}
