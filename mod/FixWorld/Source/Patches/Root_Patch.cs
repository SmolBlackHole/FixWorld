// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
	/// <summary>
	/// Hooks into the flow of the vanilla MonoBehavior.Update()
	/// </summary>
	[HarmonyPatch(typeof(Root))]
	[HarmonyPatch("Update")]
	[HarmonyPatch(new Type[0])]
	internal static class Root_Patch
	{
		[HarmonyPostfix]
		private static void UpdateHook()
		{
			FixWorldController.Instance.OnUpdate();
		}
	}
}
