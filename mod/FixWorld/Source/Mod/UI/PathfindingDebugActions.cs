using System;
using System.Reflection;
using LudeonTK;
using Verse;

namespace FixWorld.UI
{
    internal static class PathfindingDebugActions
    {
        [DebugAction("FixWorld", "Compare shadow reachability", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CompareShadowReachability()
        {
            Map map = Find.CurrentMap;
            DebugTools.curTool = new DebugTool("Start floor cell", () =>
            {
                if (Find.CurrentMap != map)
                {
                    DebugTools.curTool = null;
                    return;
                }

                IntVec3 start = Verse.UI.MouseCell();
                DebugTools.curTool = new DebugTool("Destination floor cell", () =>
                {
                    DebugTools.curTool = null;
                    if (Find.CurrentMap == map)
                        Compare(map, start, Verse.UI.MouseCell());
                });
            });
        }

        // Dev actions are discovered from active mod assemblies, not early-loaded
        // runtime assemblies. Resolve this optional test entry only when clicked.
        private static void Compare(Map map, IntVec3 start, IntVec3 target)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "FixWorld.Runtime") continue;
                MethodInfo method = assembly.GetType("FixWorld.Runtime.RuntimeHost")?
                    .GetMethod("CompareShadowCells", BindingFlags.Static | BindingFlags.NonPublic);
                if (method == null) break;
                try { method.Invoke(null, new object[] { map, start, target }); }
                catch (Exception exception) { Log.Warning("[FixWorld.ShadowTest] " + exception); }
                return;
            }
            Log.Warning("[FixWorld.ShadowTest] Matching FixWorld runtime not loaded. Restart with the current build.");
        }
    }
}
