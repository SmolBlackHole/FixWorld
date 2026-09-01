using System;
using System.Collections.Generic;
using System.Xml;
using Verse;

namespace FixWorld.Loading
{
    internal static class RimWorldStageAdapters
    {
        internal static void ClearXmlInheritance()
        {
            Run(
                LoadingStage.XmlAndPatches,
                LoadingStep.ClearXmlInheritance,
                "Clear XML inheritance",
                "Resetting inherited XML state",
                XmlInheritance.Clear);
        }

        internal static void LoadModContent(ModBootContext context)
        {
            Run(
                LoadingStage.Content,
                LoadingStep.PrepareModContent,
                "Prepare mod content",
                "Loading assemblies and queueing asset content",
                () => LoadedModManager.LoadModContent(context.HotReload));
        }

        internal static void CreateModClasses()
        {
            Run(
                LoadingStage.Content,
                LoadingStep.CreateModClasses,
                "Create mod classes",
                "Constructing RimWorld mod instances",
                LoadedModManager.CreateModClasses);
        }

        internal static List<LoadableXmlAsset> LoadModXml(
            ModBootContext context)
        {
            return Run(
                LoadingStage.XmlAndPatches,
                LoadingStep.LoadXml,
                "Load mod XML",
                "Reading XML assets in mod order",
                () => LoadedModManager.LoadModXML(context.HotReload));
        }

        internal static XmlDocument CombineXml(ModBootContext context)
        {
            return Run(
                LoadingStage.XmlAndPatches,
                LoadingStep.CombineXml,
                "Combine XML",
                "Building the unified XML document",
                () => LoadedModManager.CombineIntoUnifiedXML(
                    context.XmlAssets,
                    context.AssetLookup));
        }

        internal static void ParseTranslationKeys(ModBootContext context)
        {
            Run(
                LoadingStage.XmlAndPatches,
                LoadingStep.ParseTranslationKeys,
                "Parse translation keys",
                "Building translation-key definitions",
                () =>
                {
                    TKeySystem.Clear();
                    TKeySystem.Parse(context.Document);
                });
        }

        internal static void CheckPatches()
        {
            Run(
                LoadingStage.XmlAndPatches,
                LoadingStep.CheckPatches,
                "Check XML patches",
                "Validating patch operations",
                LoadedModManager.ErrorCheckPatches);
        }

        internal static void ApplyPatches(ModBootContext context)
        {
            Run(
                LoadingStage.XmlAndPatches,
                LoadingStep.ApplyPatches,
                "Apply XML patches",
                "Applying mod patches in load order",
                () => LoadedModManager.ApplyPatches(
                    context.Document,
                    context.AssetLookup));
        }

        internal static void ParseDefinitions(ModBootContext context)
        {
            Run(
                LoadingStage.Definitions,
                LoadingStep.ParseDefinitions,
                "Parse definitions",
                "Creating and resolving game definitions",
                () => LoadedModManager.ParseAndProcessXML(
                    context.Document,
                    context.AssetLookup,
                    context.HotReload));
        }

        internal static void ClearPatchCache()
        {
            Run(
                LoadingStage.XmlAndPatches,
                LoadingStep.ClearPatchCache,
                "Clear patch cache",
                "Releasing cached patch operations",
                LoadedModManager.ClearCachedPatches);
        }

        private static void Run(
            LoadingStage stage,
            LoadingStep step,
            string displayName,
            string activity,
            Action action)
        {
            ModBootStageRunner.Run(
                Descriptor(stage, step, displayName, activity),
                action);
        }

        private static TOutput Run<TOutput>(
            LoadingStage stage,
            LoadingStep step,
            string displayName,
            string activity,
            Func<TOutput> action)
        {
            return ModBootStageRunner.Run(
                Descriptor(stage, step, displayName, activity),
                action);
        }

        private static LoadingStageEventDescriptor Descriptor(
            LoadingStage stage,
            LoadingStep step,
            string displayName,
            string activity)
        {
            return new LoadingStageEventDescriptor(
                stage,
                step,
                displayName,
                activity,
                LoadingModAttribution.Global,
                LoadingStageEventSource.RimWorld);
        }
    }
}
