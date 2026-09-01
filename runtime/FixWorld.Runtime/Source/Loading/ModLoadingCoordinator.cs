using System;
using System.Collections.Generic;
using System.Xml;
using Verse;

namespace FixWorld.Loading
{
    internal static class ModLoadingCoordinator
    {
        internal static void Run(bool hotReload)
        {
            Log.Message(
                "[FixWorld.Runtime] Running the owned mod-loading pipeline.");
            RunStage("XmlInheritance.Clear()", XmlInheritance.Clear);
            if (!hotReload)
            {
                RunStage("InitializeMods()", LoadedModManager.InitializeMods);
            }

            LoadingSession.LoadEstimate();

            RunStage(
                "LoadModContent()",
                () => LoadedModManager.LoadModContent(hotReload));
            RunStage("CreateModClasses()", LoadedModManager.CreateModClasses);
            List<LoadableXmlAsset> xmls = RunStage(
                "LoadModXML()",
                () => LoadedModManager.LoadModXML(hotReload));
            Dictionary<XmlNode, LoadableXmlAsset> assetLookup =
                new Dictionary<XmlNode, LoadableXmlAsset>();
            XmlDocument document = RunStage(
                "CombineIntoUnifiedXML()",
                () => LoadedModManager.CombineIntoUnifiedXML(xmls, assetLookup));

            if (!hotReload)
            {
                TKeySystem.Clear();
                RunStage("TKeySystem.Parse()", () => TKeySystem.Parse(document));
                RunStage("ErrorCheckPatches()", LoadedModManager.ErrorCheckPatches);
            }

            RunStage(
                "ApplyPatches()",
                () => LoadedModManager.ApplyPatches(document, assetLookup));
            RunStage(
                "ParseAndProcessXML()",
                () => LoadedModManager.ParseAndProcessXML(
                    document,
                    assetLookup,
                    hotReload));
            RunStage("ClearCachedPatches()", LoadedModManager.ClearCachedPatches);
            RunStage("XmlInheritance.Clear()", XmlInheritance.Clear);
        }

        private static void RunStage(string name, Action action)
        {
            LongEventHandler.SetCurrentEventText("FixWorld: " + name);
            DeepProfiler.Start(name);
            try
            {
                action();
            }
            finally
            {
                DeepProfiler.End();
            }
        }

        private static T RunStage<T>(string name, Func<T> action)
        {
            LongEventHandler.SetCurrentEventText("FixWorld: " + name);
            DeepProfiler.Start(name);
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
}
