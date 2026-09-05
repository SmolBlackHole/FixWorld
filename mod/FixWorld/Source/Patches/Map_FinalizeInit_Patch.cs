// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
	/// <summary>
	/// Adds a hook to produce the MapLoaded callback for ModBase mods.
	/// </summary>
	[HarmonyPatch(typeof(Map))]
	[HarmonyPatch("FinalizeInit")]
	[HarmonyPatch(new Type[0])]
	internal static class Map_FinalizeInit_Patch
	{
		[HarmonyPostfix]
		private static void MapLoadedHook(Map __instance)
		{
			FixWorldController.Instance.OnMapInitFinalized(__instance);
		}
	}
}
