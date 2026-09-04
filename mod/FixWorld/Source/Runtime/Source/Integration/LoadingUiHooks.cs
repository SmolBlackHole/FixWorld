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
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                if (!DeferredWorkPump.RequiresIsolatedLoadingFrame)
                {
                    return true;
                }

                DrawOverlay();
                return false;
            }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix()
            {
                if (DeferredWorkPump.RequiresIsolatedLoadingFrame)
                {
                    return;
                }

                DrawOverlay();
            }

            private static void DrawOverlay()
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
