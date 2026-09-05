// SPDX-License-Identifier: MPL-2.0
using FixWorld.Utils;
using Verse;

namespace FixWorld.Core
{
	/// <summary>
	/// Checks for Dev mode and bypasses the Restart message box.
	/// Holding Shift will prevent the automatic restart.
	/// </summary>
	public static class QuickRestarter
	{
		public static bool ShowRestartDialogOutsideDevMode()
		{
			if (Prefs.DevMode)
			{
				if (!FixWorldUtility.ShiftIsHeld)
				{
					GenCommandLine.Restart();
				}
				return false;
			}
			return true;
		}
	}
}
