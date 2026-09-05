// SPDX-License-Identifier: MPL-2.0
using FixWorld.Core;
using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
    [HarmonyPatch(typeof(GenCommandLine), nameof(GenCommandLine.Restart))]
    internal static class Restart_Patch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            BootstrapIntegration.RequestRestart();
            return false;
        }
    }
}
