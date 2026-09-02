using System;
using System.Reflection;
using FixWorld.PlayData;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class PlayDataHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(DoPlayLoadPatch)
        };

        [HarmonyPatch]
        private static class DoPlayLoadPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(PlayDataLoader), "DoPlayLoad") ??
                       throw new MissingMethodException(
                           typeof(PlayDataLoader).FullName,
                           "DoPlayLoad");
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                PlayDataLoadPipeline.Run();
                return false;
            }
        }
    }
}
