// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
	/// <summary>
	/// Adds a hook to produce the MapGenerated callback for ModBase mods.
	/// </summary>
	[HarmonyPatch(typeof(MapComponentUtility))]
	[HarmonyPatch("MapGenerated")]
	[HarmonyPatch(new[] { typeof(Map) })]
	internal static class MapComponentUtility_MapGenerated_Patch
	{
		[HarmonyPostfix]
		public static void MapGeneratedHook(Map map)
		{
			FixWorldController.Instance.OnMapGenerated(map);
		}
	}
}
