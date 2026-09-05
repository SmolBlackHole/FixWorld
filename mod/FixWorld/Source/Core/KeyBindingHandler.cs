// SPDX-License-Identifier: MPL-2.0
using FixWorld.Shell;
using FixWorld.Utils;
using UnityEngine;
using Verse;

namespace FixWorld.Core
{
	/// <summary>
	/// Handles the key presses for key bindings added by FixWorld
	/// </summary>
	internal static class KeyBindingHandler
	{
		public static void OnGUI()
		{
			if (Event.current.type != EventType.KeyDown) return;
			var useEvent = false;
			if (FixWorldKeyBindings.PublishLogs.JustPressed && FixWorldUtility.ControlIsHeld)
			{
				if (FixWorldUtility.AltIsHeld)
				{
					FixWorldController.Instance.LogUploader.CopyToClipboard();
				}
				else
				{
					FixWorldController.Instance.LogUploader.ShowPublishPrompt();
				}
				useEvent = true;
			}
			if (FixWorldKeyBindings.OpenLogFile.JustPressed)
			{
				ShellOpenLog.Execute();
				useEvent = true;
			}
			if (FixWorldKeyBindings.RestartRimworld.JustPressed)
			{
				GenCommandLine.Restart();
				useEvent = true;
			}
			if (FixWorldKeyBindings.HLOpenModSettings.JustPressed)
			{
				FixWorldUtility.OpenModSettingsDialog();
				useEvent = true;
			}
			if (FixWorldKeyBindings.HLOpenUpdateNews.JustPressed)
			{
				FixWorldController.Instance.UpdateFeatures.TryShowDialog(true);
				useEvent = true;
			}
			if (useEvent)
			{
				Event.current.Use();
			}
		}
	}
}
