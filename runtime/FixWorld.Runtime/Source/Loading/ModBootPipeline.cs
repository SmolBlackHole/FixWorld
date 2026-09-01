using System.Collections.Generic;
using System.Xml;
using Verse;

namespace FixWorld.Loading
{
    internal static class ModBootPipeline
    {
        internal static void Run(bool hotReload)
        {
            Log.Message(
                "[FixWorld.Runtime] Running the owned mod-loading pipeline.");

            ModBootContext context = new ModBootContext(hotReload);
            RimWorldStageAdapters.ClearXmlInheritance();
            if (!hotReload)
            {
                LogInitialization(ModInitializationStage.Run());
            }

            LoadingSession.LoadEstimate();
            RimWorldStageAdapters.LoadModContent(context);
            RimWorldStageAdapters.CreateModClasses();
            context.XmlAssets = RimWorldStageAdapters.LoadModXml(context);
            context.Document = RimWorldStageAdapters.CombineXml(context);

            if (!hotReload)
            {
                RimWorldStageAdapters.ParseTranslationKeys(context);
                RimWorldStageAdapters.CheckPatches();
            }

            RimWorldStageAdapters.ApplyPatches(context);
            RimWorldStageAdapters.ParseDefinitions(context);
            RimWorldStageAdapters.ClearPatchCache();
            RimWorldStageAdapters.ClearXmlInheritance();
        }

        private static void LogInitialization(ModInitializationResult result)
        {
            Log.Message(
                "[FixWorld.Runtime] Initialized " + result.InitializedCount +
                " / " + result.RequestedCount +
                " active mods; disabled=" + result.DisabledCount +
                ", fallback=" + result.UsedVanillaFallback + ".");
        }
    }

    internal sealed class ModBootContext
    {
        internal ModBootContext(bool hotReload)
        {
            HotReload = hotReload;
            AssetLookup = new Dictionary<XmlNode, LoadableXmlAsset>();
        }

        internal bool HotReload { get; }

        internal List<LoadableXmlAsset> XmlAssets { get; set; }

        internal Dictionary<XmlNode, LoadableXmlAsset> AssetLookup { get; }

        internal XmlDocument Document { get; set; }
    }
}
