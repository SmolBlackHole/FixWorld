// SPDX-License-Identifier: MPL-2.0
namespace FixWorld.Settings
{
    /// <summary>
    /// Used to preserve the state of the Mod Settings window between multiple openings.
    /// State is not persisted between game restarts.
    /// </summary>
    internal class ModSettingsWindowState
    {
        private static ModSettingsWindowState instance;
        public static ModSettingsWindowState Instance
        {
            get { return instance ?? (instance = new ModSettingsWindowState()); }
        }

        public string LastSettingsPackId { get; set; }
        public float VerticalScrollPosition { get; set; }
    }
}
