// SPDX-License-Identifier: MPL-2.0
using System;
using FixWorld.Utils;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace FixWorld.Core
{
	/// <summary>
	/// Informs the player about a mod that requires a later version of FixWorld than the one running.
	/// Also has button to open the download link in the Steam or system browser.
	/// </summary>
	internal class Dialog_LibraryUpdateRequired : Window
	{
		private const string StandaloneDownloadUrl = "https://github.com/SmolBlackHole/FixWorld/releases/latest";

		private readonly TaggedString titleText;
		private readonly TaggedString bodyText;
		private readonly TaggedString updateButtonText;
		private readonly Vector2 buttonSize;

		public override Vector2 InitialSize
		{
			get { return new Vector2(500f, 400f); }
		}

		public Dialog_LibraryUpdateRequired(string requiringModName, Version requiredVersion)
		{
			titleText = "FixWorld_updateRequired_title".Translate();
			bodyText = "FixWorld_updateRequired_text".Translate()
				.Formatted(requiringModName, requiredVersion.ToSemanticString());
			updateButtonText = "FixWorld_updateRequired_updateBtn".Translate();
			buttonSize = new Vector2(
				Mathf.Max(CloseButSize.x, Text.CalcSize(updateButtonText).x + 20f), CloseButSize.y);
			closeOnCancel = true;
			doCloseButton = false;
			doCloseX = false;
			forcePause = true;
			absorbInputAroundWindow = true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			const int titleLabelHeight = 45;
			DrawTitleLabel();
			DrawMainTextLabel();
			DrawUpdateButton();
			DrawCloseButton();

			void DrawTitleLabel()
			{
				Text.Font = GameFont.Medium;
				var rect = new Rect(inRect.x, inRect.y, inRect.width, titleLabelHeight);
				Widgets.Label(rect, titleText);
			}

			void DrawMainTextLabel()
			{
				Text.Font = GameFont.Small;
				var rect = new Rect(inRect.x, inRect.y + titleLabelHeight,
					inRect.width, inRect.height - buttonSize.y - titleLabelHeight);
				Widgets.Label(rect, bodyText);
			}

			void DrawUpdateButton()
			{
				var rect = new Rect(
					inRect.x, inRect.height - buttonSize.y,
					buttonSize.x, buttonSize.y
				);
				GUI.color = Color.green;
				if (Widgets.ButtonText(rect, updateButtonText))
				{
					Close();
					OpenDownloadUrl();
				}
				GUI.color = Color.white;
			}

			void DrawCloseButton()
			{
				var rect = new Rect(
					inRect.width - buttonSize.x, inRect.height - buttonSize.y,
					buttonSize.x, buttonSize.y
				);
				if (Widgets.ButtonText(rect, "CloseButton".Translate()))
				{
					Close();
				}
			}
		}

		private static void OpenDownloadUrl()
		{
			SteamUtility.OpenUrl(StandaloneDownloadUrl);
		}
	}
}
