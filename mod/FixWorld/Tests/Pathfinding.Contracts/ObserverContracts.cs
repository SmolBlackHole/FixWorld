using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

using FixWorld.Diagnostics;
using FixWorld.Pathfinding;
using Verse;

// These tests link the production observer against these minimal stubs. They verify
// adapter behavior and telemetry contracts, not actual RimWorld hook execution.
internal static class ObserverContracts
{
    private static int assertions;

    internal static void Run()
    {
        Assert(ShadowGridObserver.Enabled,
            "The observer unexpectedly started disabled.");
        FirstIncrementalInitializesWholeMap();
        FullResampleAndNoopDeltas();
        MapsAndSeamsRemainIsolated();
        InvalidDeltasAreSkipped();
        ErrorsDisableOnlyOneMap();
        NoUpdateAllocationsAfterWarmup();
        Console.WriteLine(
            "FixWorld observer contracts passed: " + assertions + " assertions.");
    }

    internal static void RunDisabled()
    {
        Assert(!ShadowGridObserver.Enabled,
            "FIXWORLD_SHADOW_GRID=0 did not disable the observer.");
        var map = new Map(4, 4, true);
        var telemetry = new RuntimeTelemetryStore();
        var observer = new ShadowGridObserver(telemetry);
        observer.Observe(map, [], false);
        Assert(telemetry.ShadowUpdateCount == 0 && telemetry.ShadowFailureCount == 0,
            "A disabled observer touched telemetry.");
        Assert(map.WalkableReads == 0,
            "A disabled observer read a map.");
        Console.WriteLine("FixWorld observer disabled contract passed.");
    }

    private static void FirstIncrementalInitializesWholeMap()
    {
        Map map = PatternMap(9, 7);
        var telemetry = new RuntimeTelemetryStore();
        var observer = new ShadowGridObserver(telemetry);
        observer.Observe(map, [], false);
        Assert(telemetry.IncrementalUpdates == 0 && telemetry.FullUpdates == 1,
            "The first callback was not recorded as a full initialization.");
        Assert(telemetry.LastSampledCells == 63 &&
            telemetry.LastChangedCells == CountWalkable(map),
            "The first callback did not initialize the whole map.");
        Assert(telemetry.ShadowFailureCount == 0,
            "The initial observer callback failed.");
        Assert(map.WalkableReads == 63,
            "The initial callback did not sample every map cell.");
        Assert(telemetry.TimingCalls == 4,
            "The initial callback did not time update and rebuild once each.");
    }

    private static void FullResampleAndNoopDeltas()
    {
        Map map = PatternMap(8, 8);
        var telemetry = new RuntimeTelemetryStore();
        var observer = new ShadowGridObserver(telemetry);
        observer.Observe(map, [], false);
        int readsAfterInitial = map.WalkableReads;
        observer.Observe(map, null, true);
        Assert(telemetry.FullUpdates == 2 && telemetry.LastSampledCells == 64,
            "A full callback did not resample the whole map.");
        Assert(telemetry.LastChangedCells == 0,
            "A no-op full resample inflated changed cells.");
        Assert(map.WalkableReads == readsAfterInitial + 64,
            "A full callback did not read every map cell.");

        var duplicates = new List<IntVec3>
        {
            new(2, 0, 2),
            new(2, 0, 2),
            new(3, 0, 3)
        };
        int updatesBefore = telemetry.IncrementalUpdates;
        observer.Observe(map, duplicates, false);
        Assert(telemetry.IncrementalUpdates == updatesBefore + 1 &&
            telemetry.LastSampledCells == 3 && telemetry.LastChangedCells == 0,
            "Duplicate/no-op deltas inflated the incremental change count.");
    }

    private static void MapsAndSeamsRemainIsolated()
    {
        var first = new Map(17, 1, false);
        var second = new Map(17, 1, false);
        for (int x = 0; x < 15; x++)
        {
            first.SetWalkable(x, 0, true);
        }
        first.SetWalkable(16, 0, true);
        second.SetWalkable(0, 0, true);
        var telemetry = new RuntimeTelemetryStore();
        var observer = new ShadowGridObserver(telemetry);
        observer.Observe(first, [], false);
        observer.Observe(second, [], false);
        int firstReads = first.WalkableReads;
        int secondReads = second.WalkableReads;
        first.SetWalkable(15, 0, true);
        observer.Observe(first, [new(15, 0, 0)], false);
        Assert(telemetry.LastChangedCells == 1,
            "A seam bridge did not report one changed cell.");
        first.SetWalkable(7, 0, false);
        first.SetWalkable(8, 0, false);
        observer.Observe(first,
        [
            new(7, 0, 0),
            new(8, 0, 0)
        ], false);
        Assert(telemetry.LastChangedCells == 2,
            "The 7/8 seam did not report both changed cells.");
        Assert(second.WalkableReads == secondReads && first.WalkableReads > firstReads,
            "An update for one map sampled another map.");
        Assert(telemetry.ShadowFailureCount == 0,
            "A seam update failed.");
    }

    private static void InvalidDeltasAreSkipped()
    {
        var map = new Map(3, 2, false);
        var telemetry = new RuntimeTelemetryStore();
        var observer = new ShadowGridObserver(telemetry);
        observer.Observe(map, [], false);
        int reads = map.WalkableReads;
        observer.Observe(map,
        [
            new(-1, 0, 0),
            new(3, 0, 0),
            new(1, 0, 0)
        ], false);
        Assert(map.WalkableReads == reads + 1 && telemetry.LastSampledCells == 1,
            "Out-of-bounds deltas were not skipped.");
        Assert(telemetry.ShadowFailureCount == 0,
            "Invalid deltas caused an observer failure.");
    }

    private static void ErrorsDisableOnlyOneMap()
    {
        var failing = new Map(4, 4, true) { ThrowOnRead = true };
        var healthy = new Map(4, 4, true);
        var telemetry = new RuntimeTelemetryStore();
        var observer = new ShadowGridObserver(telemetry);
        observer.Observe(failing, [], false);
        observer.Observe(failing, [], false);
        Assert(telemetry.ShadowFailureCount == 1,
            "A failing map emitted more than one failure observation.");
        Assert(Log.ErrorCount == 1,
            "A failing map emitted more than one error log.");
        observer.Observe(healthy, [], false);
        Assert(telemetry.ShadowFailureCount == 1 && telemetry.ShadowUpdateCount == 1,
            "A failed map prevented another map from being observed.");
    }

    private static void NoUpdateAllocationsAfterWarmup()
    {
        var map = new Map(8, 8, true);
        var telemetry = new RuntimeTelemetryStore();
        var observer = new ShadowGridObserver(telemetry);
        var deltas = new List<IntVec3>();
        observer.Observe(map, deltas, false);
        long before = AllocatedBytes();
        observer.Observe(map, deltas, false);
        long after = AllocatedBytes();
        if (before >= 0 && after >= 0)
        {
            Assert(after - before == 0,
                "A no-op observer update allocated " + (after - before) + " bytes.");
        }
    }

    private static Map PatternMap(int width, int height)
    {
        var map = new Map(width, height, false);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                map.SetWalkable(x, z, ((x + z) & 1) == 0);
            }
        }
        return map;
    }

    private static int CountWalkable(Map map)
    {
        int count = 0;
        for (int x = 0; x < map.CellIndices.SizeX; x++)
        {
            for (int z = 0; z < map.CellIndices.SizeZ; z++)
            {
                if (map.IsWalkable(x, z))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static long AllocatedBytes() => AllocatedBytesReader();

    private static readonly Func<long> AllocatedBytesReader =
        CreateAllocatedBytesReader();

    private static Func<long> CreateAllocatedBytesReader()
    {
        MethodInfo method = typeof(GC).GetMethod(
            "GetAllocatedBytesForCurrentThread",
            BindingFlags.Public | BindingFlags.Static);
        if (method == null)
        {
            return delegate
            { return -1L; };
        }

        return (Func<long>)method.CreateDelegate(typeof(Func<long>));
    }

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

namespace Verse
{
    public struct IntVec3 : IEquatable<IntVec3>
    {
        public IntVec3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public int x;
        public int y;
        public int z;

        public readonly bool Equals(IntVec3 other) => x == other.x && y == other.y && z == other.z;

        public override readonly bool Equals(object obj) => obj is IntVec3 && Equals((IntVec3)obj);

        public override readonly int GetHashCode() => (x * 397) ^ (y * 17) ^ z;
    }

    public sealed class Map
    {
        private readonly bool[,] walkable;

        public Map(int width, int height, bool initiallyWalkable)
        {
            CellIndices = new CellIndices(width, height);
            walkable = new bool[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    walkable[x, z] = initiallyWalkable;
                }
            }
        }

        public CellIndices CellIndices { get; }

        // Match the external Verse.Map member spelling in the linked adapter.
        public CellIndices cellIndices => CellIndices;

        public bool ThrowOnRead { get; set; }

        public int WalkableReads { get; private set; }

        public bool WalkableByAny(IntVec3 cell)
        {
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("stub map read failure");
            }

            if (cell.x < 0 || cell.x >= CellIndices.SizeX ||
                cell.z < 0 || cell.z >= CellIndices.SizeZ)
            {
                throw new ArgumentOutOfRangeException(nameof(cell));
            }

            WalkableReads++;
            return walkable[cell.x, cell.z];
        }

        public bool IsWalkable(int x, int z) => walkable[x, z];

        public void SetWalkable(int x, int z, bool value) => walkable[x, z] = value;

        public void ResetReadCount() => WalkableReads = 0;

        internal bool ReadWalkable(IntVec3 cell)
        {
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("stub map read failure");
            }

            if (cell.x < 0 || cell.x >= CellIndices.SizeX ||
                cell.z < 0 || cell.z >= CellIndices.SizeZ)
            {
                throw new ArgumentOutOfRangeException(nameof(cell));
            }

            WalkableReads++;
            return walkable[cell.x, cell.z];
        }
    }

    public sealed class CellIndices
    {
        internal CellIndices(int sizeX, int sizeZ)
        {
            SizeX = sizeX;
            SizeZ = sizeZ;
        }

        public int SizeX { get; }
        public int SizeZ { get; }
    }

    public static class GenGrid
    {
        public static bool WalkableByAny(IntVec3 cell, Map map) =>
            map.ReadWalkable(cell);
    }

    public static class Log
    {
        public static int ErrorCount { get; private set; }

        public static void Error(string message) => ErrorCount++;
    }
}

namespace FixWorld.Diagnostics
{
    internal enum RuntimeHotpath
    {
        ShadowGridFull,
        ShadowGridIncremental,
        ShadowGridRebuild
    }

    internal sealed class RuntimeTelemetryStore
    {
        internal int FullUpdates { get; private set; }
        internal int IncrementalUpdates { get; private set; }
        internal int ShadowUpdateCount => FullUpdates + IncrementalUpdates;
        internal int ShadowFailureCount { get; private set; }
        internal int LastSampledCells { get; private set; }
        internal int LastChangedCells { get; private set; }
        internal int TimingCalls { get; private set; }

        internal long StartRuntimeHotpath(RuntimeHotpath hotpath)
        {
            TimingCalls++;
            return Stopwatch.GetTimestamp();
        }

        internal void StopRuntimeHotpath(RuntimeHotpath hotpath, long startedAt) => TimingCalls++;

        internal void ObserveShadowGrid(
            bool fullRebuild,
            int sampledCells,
            int changedCells,
            in ShadowRebuildStats stats)
        {
            if (fullRebuild)
            {
                FullUpdates++;
            }
            else
            {
                IncrementalUpdates++;
            }

            LastSampledCells = sampledCells;
            LastChangedCells = changedCells;
        }

        internal void ObserveShadowGridFailure() => ShadowFailureCount++;
    }
}
