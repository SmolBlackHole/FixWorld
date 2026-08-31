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
        private static readonly object AccessorSync = new object();
        private static readonly Dictionary<Type, ClosureAccessor> Accessors =
            new Dictionary<Type, ClosureAccessor>();
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

        private readonly ModContentPack mod;

        internal ModContentPack Mod => mod;

        private ContentLoadingPipeline(ModContentPack mod)
        {
            this.mod = mod;
        }

        internal static bool MatchesContract(MethodInfo method)
        {
            return method != null &&
                   method.Name == "<ReloadContent>b__0" &&
                   method.DeclaringType?.DeclaringType == typeof(ModContentPack);
        }

        internal static bool TryCreateCompatible(
            Action action,
            out ContentLoadingPipeline pipeline)
        {
            pipeline = null;
            if (action == null ||
                !MatchesContract(action.Method) ||
                ReloadContentMethod == null ||
                AssetNamesCacheField == null ||
                AssetNamesTrieCacheField == null ||
                HasHarmonyPatches())
            {
                return false;
            }

            object target = action.Target;
            if (target == null)
            {
                return false;
            }

            ClosureAccessor accessor = GetAccessor(target.GetType());
            ModContentPack targetMod = accessor.ModField?.GetValue(target) as ModContentPack;
            if (targetMod == null ||
                accessor.HotReloadField == null ||
                (bool)accessor.HotReloadField.GetValue(target))
            {
                return false;
            }

            pipeline = new ContentLoadingPipeline(targetMod);
            return true;
        }

        internal LoadingActionPlan CreatePlan(string label)
        {
            ContentLoadingStep[] steps =
            {
                new ContentLoadingStep(
                    LoadingStep.LoadAudio,
                    "Audio",
                    "Reload audio clips",
                    () => mod.GetContentHolder<AudioClip>().ReloadAll(false)),
                new ContentLoadingStep(
                    LoadingStep.LoadTextures,
                    "Textures",
                    "Reload textures",
                    () => mod.GetContentHolder<Texture2D>().ReloadAll(false)),
                new ContentLoadingStep(
                    LoadingStep.LoadStrings,
                    "Strings",
                    "Reload strings",
                    () => mod.GetContentHolder<string>().ReloadAll(false)),
                new ContentLoadingStep(
                    LoadingStep.LoadAssetBundles,
                    "Asset bundles",
                    "Reload asset bundles",
                    () => ReloadAssetBundles(mod))
            };
            LoadingModAttribution attribution = LoadingModAttribution.Exact(mod);
            LoadingPipelineStage[] stages = new LoadingPipelineStage[steps.Length];
            for (int index = 0; index < steps.Length; index++)
            {
                ContentLoadingStep step = steps[index];
                LoadingWorkItem item = new LoadingWorkItem(
                    LoadingStage.Content,
                    step.Operation,
                    "Loading content for " + mod.Name,
                    step.DisplayName + "   " + (index + 1) + " / " + steps.Length,
                    step.ProfilerLabel,
                    step.DisplayName,
                    attribution,
                    index + 1,
                    steps.Length,
                    continueOnFailure: false,
                    execute: step.Execute);
                stages[index] = new LoadingPipelineStage(
                    index,
                    step.DisplayName,
                    LoadingStage.Content,
                    step.Operation,
                    LoadingExecutionMode.MainThread,
                    item,
                    index == 0 ? null : new[] { index - 1 });
            }

            return new LoadingActionPlan(
                label,
                attribution,
                stages);
        }

        private static ClosureAccessor GetAccessor(Type targetType)
        {
            lock (AccessorSync)
            {
                if (Accessors.TryGetValue(targetType, out ClosureAccessor accessor))
                {
                    return accessor;
                }

                FieldInfo[] fields = targetType.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
                accessor = new ClosureAccessor(
                    fields.FirstOrDefault(field =>
                        typeof(ModContentPack).IsAssignableFrom(field.FieldType)),
                    fields.FirstOrDefault(field => field.FieldType == typeof(bool)));
                Accessors.Add(targetType, accessor);
                return accessor;
            }
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

        private static void ReloadAssetBundles(ModContentPack targetMod)
        {
            targetMod.assetBundles.ReloadAll(false);
            AssetNamesCacheField.SetValue(targetMod, null);
            AssetNamesTrieCacheField.SetValue(targetMod, null);
        }

        private readonly struct ContentLoadingStep
        {
            internal readonly LoadingStep Operation;
            internal readonly string DisplayName;
            internal readonly string ProfilerLabel;
            internal readonly Action Execute;

            internal ContentLoadingStep(
                LoadingStep operation,
                string displayName,
                string profilerLabel,
                Action execute)
            {
                Operation = operation;
                DisplayName = displayName;
                ProfilerLabel = profilerLabel;
                Execute = execute;
            }
        }

        private sealed class ClosureAccessor
        {
            internal readonly FieldInfo ModField;
            internal readonly FieldInfo HotReloadField;

            internal ClosureAccessor(FieldInfo modField, FieldInfo hotReloadField)
            {
                ModField = modField;
                HotReloadField = hotReloadField;
            }
        }
    }
}
