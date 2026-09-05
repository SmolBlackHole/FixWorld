// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
	/// <summary>
	/// Adds a hook for the early initialization of a Game.
	/// </summary>
	[HarmonyPatch(typeof(Game))]
	[HarmonyPatch("FillComponents")]
	internal static class Game_FillComponents_Patch
	{
		[HarmonyPrefix]
		public static void GameInitializationHook(Game __instance)
		{
			FixWorldController.Instance.OnGameInitializationStart(__instance);
		}
	}
}
