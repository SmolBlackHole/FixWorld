// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
	/// <summary>
	/// Adds a hook to produce the DefsLoaded callback for ModBase mods.
	/// </summary>
	[HarmonyPatch(typeof(PlayDataLoader))]
	[HarmonyPatch("DoPlayLoad")]
	[HarmonyPatch(new Type[0])]
	internal static class PlayDataLoader_Patch
	{
		[HarmonyPostfix]
		private static void InitModsHook()
		{
			FixWorldController.Instance.LoadReloadInitialize();
		}
	}
}
