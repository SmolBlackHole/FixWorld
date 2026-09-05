// SPDX-License-Identifier: MPL-2.0
using RimWorld;
using Verse;
// ReSharper disable UnassignedField.Global

namespace FixWorld.Core
{
	/// <summary>
	/// Holds references to key binding defs used by the library.
	/// </summary>
	[DefOf]
	public static class FixWorldKeyBindings
	{
		public static KeyBindingDef PublishLogs;
		public static KeyBindingDef OpenLogFile;
		public static KeyBindingDef RestartRimworld;
		public static KeyBindingDef HLOpenModSettings;
		public static KeyBindingDef HLOpenUpdateNews;
	}
}
