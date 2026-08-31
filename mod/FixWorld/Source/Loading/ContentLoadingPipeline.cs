using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace FixWorld.Loading
{
    internal sealed class ContentLoadingPipeline
    {
        private static readonly MethodInfo ReloadContentMethod = AccessTools.Method(
            typeof(ModContentPack),
            "ReloadContentInt",
            new[] { typeof(bool) });
        private static readonly FieldInfo AssetNamesCacheField = AccessTools.Field(
            typeof(ModContentPack),
            "allAssetNamesInBundleCached");
        private static readonly FieldInfo AssetNamesTrieCacheField = AccessTools.Field(
            typeof(ModContentPack),
            "allAssetNamesInBundleCachedTrie");
        internal ModContentPack Mod { get; }
        internal IReadOnlyList<ContentLoadingStep> Steps { get; }

        private ContentLoadingPipeline(ModContentPack mod)
        {
            Mod = mod;
            Steps = new[]
            {
                new ContentLoadingStep(
                    "Audio",
                    "Reload audio clips",
                    () => mod.GetContentHolder<AudioClip>().ReloadAll(false)),
                new ContentLoadingStep(
                    "Textures",
                    "Reload textures",
                    () => mod.GetContentHolder<Texture2D>().ReloadAll(false)),
                new ContentLoadingStep(
                    "Strings",
                    "Reload strings",
                    () => mod.GetContentHolder<string>().ReloadAll(false)),
                new ContentLoadingStep(
                    "Asset bundles",
                    "Reload asset bundles",
                    () => ReloadAssetBundles(mod))
            };
        }

        internal static bool TryCreate(
            Action action,
            out ContentLoadingPipeline pipeline)
        {
            pipeline = null;
            bool reloadAction = action.Method.Name == "<ReloadContent>b__0";
            bool declaringTypeMatches =
                action.Method.DeclaringType?.DeclaringType == typeof(ModContentPack);
            bool hasHarmonyPatches = ReloadContentMethod != null && HasHarmonyPatches();
            if (ReloadContentMethod == null ||
                AssetNamesCacheField == null ||
                AssetNamesTrieCacheField == null ||
                !reloadAction ||
                !declaringTypeMatches ||
                hasHarmonyPatches)
            {
                return false;
            }

            object target = action.Target;
            if (target == null)
            {
                return false;
            }

            FieldInfo[] fields = target.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            ModContentPack mod = fields
                .Where(field => typeof(ModContentPack).IsAssignableFrom(field.FieldType))
                .Select(field => field.GetValue(target) as ModContentPack)
                .FirstOrDefault(value => value != null);
            FieldInfo hotReloadField = fields.FirstOrDefault(field => field.FieldType == typeof(bool));
            if (mod == null ||
                hotReloadField == null ||
                (bool)hotReloadField.GetValue(target))
            {
                return false;
            }

            pipeline = new ContentLoadingPipeline(mod);
            return true;
        }

        private static bool HasHarmonyPatches()
        {
            Patches patches = Harmony.GetPatchInfo(ReloadContentMethod);
            return patches != null &&
                   (patches.Prefixes.Count > 0 ||
                    patches.Postfixes.Count > 0 ||
                    patches.Transpilers.Count > 0 ||
                    patches.Finalizers.Count > 0);
        }

        private static void ReloadAssetBundles(ModContentPack mod)
        {
            mod.assetBundles.ReloadAll(false);
            AssetNamesCacheField.SetValue(mod, null);
            AssetNamesTrieCacheField.SetValue(mod, null);
        }
    }

    internal readonly struct ContentLoadingStep
    {
        internal readonly string DisplayName;
        internal readonly string ProfilerLabel;
        internal readonly Action Execute;

        internal ContentLoadingStep(
            string displayName,
            string profilerLabel,
            Action execute)
        {
            DisplayName = displayName;
            ProfilerLabel = profilerLabel;
            Execute = execute;
        }
    }
}
