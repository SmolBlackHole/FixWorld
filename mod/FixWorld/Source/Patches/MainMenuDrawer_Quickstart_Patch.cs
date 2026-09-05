// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using FixWorld.Quickstart;
using RimWorld;

namespace FixWorld.Patches
{
	/// <summary>
	/// Rewire the main menu "Dev quicktest" button to trigger the FixWorld quickstarter.
	/// </summary>
	[HarmonyPatch(typeof(MainMenuDrawer))]
	[HarmonyPatch(nameof(MainMenuDrawer.DoMainMenuControls))]
	internal class MainMenuDrawer_Quickstart_Patch
	{
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> QuicktestButtonUsesQuickstarter(
			IEnumerable<CodeInstruction> instructions)
		{
			return new CodeMatcher(instructions)
				.MatchStartForward(new CodeMatch(OpCodes.Ldstr, "DevQuickTest"))
				.MatchStartForward(new CodeMatch(OpCodes.Ldftn))
				.SetOperandAndAdvance(new Action(QuickstartController.InitiateMapGeneration).Method)
				.InstructionEnumeration();
		}
	}
}
