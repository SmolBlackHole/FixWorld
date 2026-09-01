using System;
using FixWorld.Loading;
using FixWorld.Preloader;
using FixWorld.Runtime;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class ModBootHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(LoadAllActiveModsPatch)
        };

        [HarmonyPatch(
            typeof(LoadedModManager),
            nameof(LoadedModManager.LoadAllActiveMods),
            new[] { typeof(bool) })]
        private static class LoadAllActiveModsPatch
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(bool hotReload)
            {
                if (!RuntimeHost.BeginModBoot())
                {
                    return true;
                }

                PreloaderTimelineContract.PublishRuntimeOwnsModBoot();
                ModBootPipeline.Run(hotReload);
                return false;
            }
        }
    }
}
