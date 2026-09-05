// SPDX-License-Identifier: MPL-2.0
using System;
using System.IO;
using UnityEngine;
using Verse;

namespace FixWorld.News
{
    [StaticConstructorOnStartup]
    internal static class UpdateFeatureImageLoader
    {
        private const string UpdateFeatureImageBaseFolder = UpdateFeatureManager.UpdateFeatureDefFolder;

        private static readonly string[] PossibleTextureFileExtensions = {
            ".png",
            ".jpg",
            ".jpeg",
            ".psd"
        };

        private static readonly Texture2D missingTexturePlaceholder = ContentFinder<Texture2D>.Get(BaseContent.BadTexPath);

        internal static NewsImage GetImage(ModContentPack modContent, string relativeFilePathNoExtension)
        {
            try
            {
                var newsFolderTex = TryResolveTextureRelativeToNewsFolder(modContent, relativeFilePathNoExtension);
                if (newsFolderTex != null) return new NewsImage(newsFolderTex, owned: true);
                // try getting the texture from the common resources as fallback
                var resourcesTex = ContentFinder<Texture2D>.Get(relativeFilePathNoExtension, false);
                if (resourcesTex != null) return new NewsImage(resourcesTex, owned: false);
            }
            catch (Exception e)
            {
                FixWorldController.Logger.Warning("Exception while loading texture: " + e);
            }
            // if all else fails, return purple "missing image" texture
            FixWorldController.Logger.Warning($"Failed to resolve update feature texture mod:{modContent.PackageIdPlayerFacing} " +
                                            $"file:{relativeFilePathNoExtension}, using placeholder");
            return new NewsImage(missingTexturePlaceholder, owned: false);
        }

        private static Texture2D TryResolveTextureRelativeToNewsFolder(ModContentPack modContent, string relativeFilePathNoExtension)
        {
            var modSpecificNewsFolderPath = Path.Combine(modContent.RootDir, UpdateFeatureImageBaseFolder);
            if (Directory.Exists(modSpecificNewsFolderPath))
            {
                var imageFilePathNoExtension = Path.Combine(modSpecificNewsFolderPath, relativeFilePathNoExtension);
                foreach (var possibleFileExtension in PossibleTextureFileExtensions)
                {
                    var newsFolderImageFileInfo = new FileInfo(imageFilePathNoExtension + possibleFileExtension);
                    if (newsFolderImageFileInfo.Exists)
                    {
                        var modContentTex = LoadTextureFromFile(newsFolderImageFileInfo);
                        return modContentTex;
                    }
                }
            }
            return null;
        }

        private static Texture2D LoadTextureFromFile(FileInfo file)
        {
            Texture2D tex = null;
            try
            {
                var fileBytes = File.ReadAllBytes(file.FullName);
                tex = new Texture2D(2, 2, TextureFormat.Alpha8, true);
                if (!tex.LoadImage(fileBytes)) throw new IOException("Image decoding failed.");
                tex.name = Path.GetFileNameWithoutExtension(file.Name);
                tex.Compress(true);
                tex.filterMode = FilterMode.Bilinear;
                tex.anisoLevel = 2;
                tex.Apply(true, true);
                return tex;
            }
            catch (Exception e)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                throw new IOException($"Failed to load texture at path \"{file.FullName}\"", e);
            }
        }
    }
}
