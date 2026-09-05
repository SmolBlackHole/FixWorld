// SPDX-License-Identifier: MPL-2.0
using Verse;

namespace FixWorld.Core
{
	/// <summary>
	/// Forwards ticks to the controller. Will not be saved and is never spawned.
	/// </summary>
	public class FixWorldTickProxy : Thing
	{
		// a precaution against ending up in a save. Shouldn't happen, as it is never spawned.
		public bool CreatedByController { get; internal set; }

		public FixWorldTickProxy()
		{
			def = new ThingDef { tickerType = TickerType.Normal, isSaveable = false };
		}

		protected override void Tick()
		{
			if (CreatedByController) FixWorldController.Instance.OnTick();
		}
	}
}
