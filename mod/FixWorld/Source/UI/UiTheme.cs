// SPDX-License-Identifier: MPL-2.0
using UnityEngine;

namespace FixWorld.UI
{
    // Colors only: safe before ContentFinder and the library's icon textures exist.
    internal static class UiTheme
    {
        internal static readonly Color Accent = new(0.25f, 0.73f, 0.90f);
        internal static readonly Color Completed = new(0.16f, 0.48f, 0.68f);
        internal static readonly Color Row = new(1f, 1f, 1f, 0.035f);
        internal static readonly Color Pending = new(1f, 1f, 1f, 0.14f);
        internal static readonly Color Muted = new(0.68f, 0.72f, 0.75f);
        internal static readonly Color Warning = new(1f, 0.73f, 0.36f);
    }
}
