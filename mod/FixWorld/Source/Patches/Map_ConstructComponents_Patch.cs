// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
	/// <summary>
	/// Adds a hook to produce the MapComponentsInitializing callback for ModBase mods.
	/// </summary>
	[HarmonyPatch(typeof(Map))]
	[HarmonyPatch("ConstructComponents")]
	[HarmonyPatch(new Type[0])]
	internal static class Map_ConstructComponents_Patch
	{
		[HarmonyPostfix]
		private static void MapComponentsInitHook(Map __instance)
		{
			FixWorldController.Instance.OnMapComponentsConstructed(__instance);
		}
	}
}
