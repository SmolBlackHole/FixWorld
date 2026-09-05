// SPDX-License-Identifier: MPL-2.0
using System;
using FixWorld.Utils;
using Verse;

namespace FixWorld.Core
{
	internal static class LoadOrderChecker
	{
		private const string LibraryModName = "FixWorld";
		/// <summary>
		/// Ensures that the library comes after Core in the load order and displays a warning dialog otherwise.
		/// </summary>
		public static void ValidateLoadOrder()
		{
			try
			{
				var coreFound = false;
				foreach (var pack in LoadedModManager.RunningMods)
				{
					if (pack.IsCoreMod)
					{
						coreFound = true;
					}
					else if (pack.Name == LibraryModName)
					{
						if (!coreFound)
						{
							ScheduleWarningDialog();
						}
						break;
					}
				}
			}
			catch (Exception e)
			{
				FixWorldController.Logger.ReportException(e);
			}
		}

		private static void ScheduleWarningDialog()
		{
			// make sure this executes after this WindowStack is initialized
			LongEventHandler.QueueLongEvent(() =>
			{
				Find.WindowStack.Add(new Dialog_Message("FixWorld_loadOrderWarning_title".Translate(), "FixWorld_loadOrderWarning_text".Translate()));
			}, null, false, null);
		}
	}
}
