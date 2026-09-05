// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
	/// <summary>
	/// Adds a hook for discarding maps.
	/// </summary>
	[HarmonyPatch(typeof(Game))]
	[HarmonyPatch("DeinitAndRemoveMap")]
	[HarmonyPatch(new[] { typeof(Map), typeof(bool) })]
	internal static class Game_DeinitAndRemoveMap_Patch
	{
		[HarmonyPostfix]
		private static void MapRemovalHook(Map map)
		{
			FixWorldController.Instance.OnMapDiscarded(map);
		}
	}
}
