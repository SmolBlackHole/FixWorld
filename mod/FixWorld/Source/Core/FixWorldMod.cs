// SPDX-License-Identifier: MPL-2.0
using Verse;

namespace FixWorld.Core
{
	/// <summary>
	/// Entry point for the library.
	/// Instantiated by the game at the start of DoPlayLoad().
	/// </summary>
	public class FixWorldMod : Mod
	{
		public FixWorldMod(ModContentPack content) : base(content)
		{
			FixWorldController.EarlyInitialize(content);
		}
	}
}
