// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using UnityEngine;
using Verse;

// suppress unassigned field warning
#pragma warning disable 649

namespace FixWorld.Core
{
	/// <summary>
	/// Loads and stores textures from the FixWorld /Textures folder
	/// </summary>
	[StaticConstructorOnStartup]
	internal static class FixWorldTextures
	{
		public static Texture2D quickstartIcon;
		public static Texture2D HLMenuIcon;
		public static Texture2D HLMenuIconPlus;
		public static Texture2D HLInfoIcon;

		static FixWorldTextures()
		{
			foreach (var fieldInfo in typeof(FixWorldTextures).GetFields(BindingFlags.Public | BindingFlags.Static))
			{
				fieldInfo.SetValue(null, ContentFinder<Texture2D>.Get(fieldInfo.Name));
			}
		}
	}
}
