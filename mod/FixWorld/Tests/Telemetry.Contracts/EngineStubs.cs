// SPDX-License-Identifier: MPL-2.0
// Only the engine boundary is stubbed. Scheduler, profiler and telemetry are
// linked directly from production source.
using System;
namespace Verse { public class Thing { public bool Destroyed { get; set; } public bool Spawned { get; set; } = true; } }
namespace UnityEngine
{
    public static class Mathf { public static int Min(int a, int b) => Math.Min(a, b); public static int CeilToInt(float a) => (int)Math.Ceiling(a); }
}
namespace FixWorld
{
    public static class FixWorldController { public static StubLogger Logger { get; } = new(); }
    public sealed class StubLogger
    {
        public void Warning(string value, params object[] args) => throw new Exception(value);
        public void Error(string value, params object[] args) => throw new Exception(value);
    }
}
namespace FixWorld.Utils { public static class FixWorldUtility { public static string DescribeDelegate(Delegate value) => value.Method.Name; } }
