using System;
using System.Collections.Generic;

namespace UnityEngine { public struct Rect { } }
namespace FixWorld.Utils { }
namespace Verse
{
    internal static class SettingsExtensions
    {
        internal static bool CanTranslate(this string value) => true;
        internal static string Join(this IEnumerable<string> values, string separator) => string.Join(separator, values);
    }
}
namespace FixWorld.Core
{
    internal static class PersistentDataManager
    {
        internal static bool IsValidElementName(string value) => !string.IsNullOrWhiteSpace(value);
    }
}
namespace FixWorld.Settings
{
    public sealed class ContextMenuEntry { }
    internal sealed class ModSettingsManager
    {
        internal void SaveChanges() { }
    }
}
namespace FixWorld
{
    internal static class FixWorldController
    {
        internal static readonly TestLogger Logger = new TestLogger();
    }
    internal sealed class TestLogger
    {
        internal void Warning(string message) { }
        internal void Error(string message, params object[] args) { }
        internal void ReportException(Exception error) { }
    }
}
