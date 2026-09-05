// SPDX-License-Identifier: MPL-2.0
using Verse;

namespace FixWorld.Core
{
    /// <summary>
    /// Provides an entry point for late controller setup during static constructor initialization.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class StaticInitializer
    {
        static StaticInitializer()
        {
            if (FixWorld.Bootstrap.BootSession.Current.IsAttached)
                FixWorldController.Instance.LateInitialize();
        }
    }
}
