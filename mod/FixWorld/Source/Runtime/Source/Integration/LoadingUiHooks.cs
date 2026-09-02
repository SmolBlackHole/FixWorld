using System;
using FixWorld.PlayData;
using FixWorld.Runtime;
using FixWorld.UI;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class LoadingUiHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(LoadingOverlayPatch)
        };

        [HarmonyPatch(
            typeof(LongEventHandler),
            nameof(LongEventHandler.LongEventsOnGUI))]
        private static class LoadingOverlayPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (RuntimeHost.TryGetLoadingSnapshot(
                        out PlayDataLoadingSnapshot snapshot))
                {
                    LoadingProgressUi.Draw(snapshot);
                }
            }
        }
    }
}
