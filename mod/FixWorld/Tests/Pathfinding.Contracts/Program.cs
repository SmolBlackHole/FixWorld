using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

using FixWorld.Pathfinding;

internal static class Program
{
    private static int assertions;

    private static int Main(string[] arguments)
    {
        try
        {
            if (arguments.Length == 1 &&
                string.Equals(arguments[0], "--benchmark", StringComparison.Ordinal))
            {
                RunBenchmark();
                return 0;
            }

            if (arguments.Length == 1 &&
                string.Equals(arguments[0], "--observer-disabled", StringComparison.Ordinal))
            {
                ObserverContracts.RunDisabled();
                return 0;
            }

            if (arguments.Length != 0)
            {
                throw new ArgumentException(
                    "Usage: FixWorld.Pathfinding.Contracts [--benchmark|--observer-disabled]");
            }

            RunContracts();
            ObserverContracts.Run();
            Console.WriteLine(
                "FixWorld pathfinding contracts passed: " +
                assertions.ToString("N0", CultureInfo.InvariantCulture) +
                " assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RunContracts()
    {
        InputContracts();
        PatternContracts();
        DimensionContracts();
        IncrementalContracts();
        PartialSuperChunkContracts();
        RandomContracts();
    }

    private static void InputContracts()
    {
        AssertThrows<ArgumentOutOfRangeException>(
            () => new ShadowConnectivityGrid(0, 1));
        AssertThrows<ArgumentOutOfRangeException>(
            () => new ShadowConnectivityGrid(1, 0));
        ShadowConnectivityGrid grid = new ShadowConnectivityGrid(4, 3);
        Assert(!grid.IsWalkable(0, 0), "A new grid was not initially blocked.");
        Assert(!grid.SetWalkable(0, 0, false), "An unchanged blocked cell was changed.");
        Assert(grid.SetWalkable(0, 0, true), "A blocked cell did not become walkable.");
        Assert(grid.IsWalkable(0, 0), "The walkable edit was not retained.");
        AssertThrows<ArgumentOutOfRangeException>(
            () => grid.IsWalkable(-1, 0));
        AssertThrows<ArgumentOutOfRangeException>(
            () => grid.IsWalkable(4, 0));
        AssertThrows<ArgumentOutOfRangeException>(
            () => grid.SetWalkable(0, 3, true));
        ShadowRebuildStats initial = grid.Rebuild();
        Assert(initial.RebuiltLeaves == 1 && initial.RebuiltRegions == 1 &&
            initial.RebuiltSuperChunks == 1,
            "The first rebuild did not build the all-blocked hierarchy.");
        AssertThrows<ArgumentOutOfRangeException>(
            () => grid.GetComponent(0, 0, 7));
        AssertThrows<ArgumentOutOfRangeException>(
            () => grid.GetComponent(0, 0, 64));
        Assert(grid.GetComponent(0, 0, 8) >= 0,
            "A walkable cell had no component after rebuild.");
        Assert(grid.SetWalkable(1, 0, true), "A pending edit was not accepted.");
        foreach (int chunkSize in new[] { 8, 16, 32 })
        {
            AssertThrows<InvalidOperationException>(
                () => grid.GetComponent(0, 0, chunkSize));
        }

        ShadowRebuildStats first = grid.Rebuild();
        Assert(first.RebuiltLeaves > 0 && first.ChangedLeaves > 0,
            "An edit did not rebuild changed leaves.");
        Assert(first.RebuiltRegions > 0 && first.ChangedRegions > 0,
            "An edit did not rebuild changed regions.");
        Assert(first.RebuiltSuperChunks > 0 && first.ChangedSuperChunks > 0,
            "An edit did not rebuild changed super-chunks.");
        long generation = grid.Generation;
        ShadowRebuildStats noOp = grid.Rebuild();
        Assert(generation == grid.Generation, "A no-op rebuild advanced generation.");
        AssertStatsZero(noOp, "A no-op rebuild reported work.");
        Assert(!grid.SetWalkable(0, 0, true), "An idempotent edit reported a change.");
        AssertStatsZero(grid.Rebuild(), "An unchanged edit reported rebuild work.");
    }

    private static void PatternContracts()
    {
        foreach (int chunkSize in new[] { 8, 16, 32 })
        {
            CheckPattern(9, 7, chunkSize, delegate (int x, int z)
            { return false; });
            CheckPattern(9, 7, chunkSize, delegate (int x, int z)
            { return true; });
            CheckPattern(17, 17, chunkSize,
                delegate (int x, int z)
                { return ((x + z) & 1) == 0; });
            CheckPattern(17, 17, chunkSize,
                delegate (int x, int z)
                { return x == z; });
            CheckPattern(17, 17, chunkSize,
                delegate (int x, int z)
                { return (x & 1) == 0 && (z & 1) == 0; });
            CheckPattern(17, 17, chunkSize,
                delegate (int x, int z)
                { return x == 8 || z == 8; });
        }

        bool[,] bridge = Filled(17, 17, true);
        for (int z = 0; z < 17; z++)
        {
            bridge[8, z] = false;
        }
        bridge[8, 8] = true;
        CheckMap(bridge, 8);
        bridge[8, 8] = false;
        CheckMap(bridge, 8);
        bridge[8, 4] = true;
        CheckMap(bridge, 8);
        bridge[8, 8] = true;
        CheckMap(bridge, 8);
    }

    private static void DimensionContracts()
    {
        int[] dimensions = { 1, 7, 8, 9, 16, 17, 31, 32, 33 };
        foreach (int width in dimensions)
        {
            foreach (int height in new[] { 1, 7, 9, 17, 31, 32, 33 })
            {
                int localWidth = width;
                int localHeight = height;
                CheckPattern(localWidth, localHeight, 8,
                    delegate (int x, int z)
                    {
                        return x == 0 || z == 0 || x == localWidth - 1 ||
                            z == localHeight - 1;
                    });
            }
        }
    }

    private static void IncrementalContracts()
    {
        bool[,] map = Filled(32, 32, true);
        ShadowConnectivityGrid grid = Build(map);
        AssertBoundaryPartition(grid, map, 8, "full map boundary");

        int[] internalX = { 3, 4, 5, 6 };
        foreach (int x in internalX)
        {
            map[x, 3] = false;
            Assert(grid.SetWalkable(x, 3, false), "An internal edit was ignored.");
        }
        ShadowRebuildStats split = grid.Rebuild();
        Assert(split.RebuiltLeaves == 1 && split.ChangedLeaves > 0,
            "An internal split did not rebuild changed leaves.");
        Assert(split.RebuiltRegions == 1 && split.ChangedRegions == 0 &&
            split.RebuiltSuperChunks == 0 && split.ChangedSuperChunks == 0,
            "An internal edit propagated beyond its unchanged region boundary.");
        AssertBoundaryPartition(grid, map, 8, "internal split boundary");
        AssertEquivalentToFresh(grid, map, 8, "internal split");
        AssertEquivalentAtAllLevels(grid, map, "internal split all levels");

        map[4, 3] = true;
        Assert(grid.SetWalkable(4, 3, true), "A bridge edit was ignored.");
        grid.Rebuild();
        AssertEquivalentToFresh(grid, map, 8, "internal bridge");
        AssertEquivalentAtAllLevels(grid, map, "internal bridge all levels");

        map[7, 3] = false;
        Assert(grid.SetWalkable(7, 3, false), "A leaf-boundary edit was ignored.");
        ShadowRebuildStats boundary = grid.Rebuild();
        Assert(boundary.RebuiltLeaves == 2 && boundary.RebuiltRegions == 1 &&
            boundary.ChangedRegions == 0 && boundary.RebuiltSuperChunks == 0,
            "A leaf-boundary edit propagated unexpectedly (rebuilt=" +
            boundary.RebuiltLeaves + "/" + boundary.RebuiltRegions + "/" +
            boundary.RebuiltSuperChunks + ").");
        AssertEquivalentToFresh(grid, map, 8, "leaf-boundary edit");

        bool[,] reverted = Filled(16, 16, true);
        ShadowConnectivityGrid revertedGrid = Build(reverted);
        Assert(revertedGrid.SetWalkable(3, 3, false), "A revert edit was ignored.");
        Assert(revertedGrid.SetWalkable(3, 3, true), "A revert restore was ignored.");
        ShadowRebuildStats revertedStats = revertedGrid.Rebuild();
        Assert(revertedStats.RebuiltLeaves == 1 && revertedStats.RebuiltRegions == 0 &&
            revertedStats.RebuiltSuperChunks == 0,
            "An edit reverted before rebuild propagated to parent levels.");
        AssertStatsChangedZero(revertedStats,
            "An edit reverted before rebuild reported changed summaries.");

        SeamContracts();

        bool[,] repeated = Clone(map);
        for (int index = 0; index < 30; index++)
        {
            int x = (index * 7 + 2) % 32;
            int z = (index * 11 + 5) % 32;
            bool value = (index & 1) == 0;
            bool wasWalkable = grid.IsWalkable(x, z);
            repeated[x, z] = value;
            Assert(grid.SetWalkable(x, z, value) == (wasWalkable != value),
                "SetWalkable changed-state contract was inconsistent.");
            if ((index % 3) == 2)
            {
                grid.Rebuild();
                AssertEquivalentToFresh(grid, repeated, 8,
                    "repeated incremental edit " + index);
            }
        }
        grid.Rebuild();
        AssertEquivalentToFresh(grid, repeated, 8, "repeated incremental final");
    }

    private static void RandomContracts()
    {
        Random random = new Random(668);
        for (int sample = 0; sample < 18; sample++)
        {
            int width = 1 + random.Next(34);
            int height = 1 + random.Next(34);
            bool[,] map = new bool[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    map[x, z] = random.Next(100) < 63;
                }
            }

            int chunkSize = new[] { 8, 16, 32 }[sample % 3];
            ShadowConnectivityGrid grid = Build(map);
            AssertEquivalentToFresh(grid, map, chunkSize, "random initial " + sample);
            for (int edit = 0; edit < 18; edit++)
            {
                int x = random.Next(width);
                int z = random.Next(height);
                bool value = random.Next(2) == 0;
                bool changed = grid.SetWalkable(x, z, value);
                Assert(changed == (map[x, z] != value),
                    "SetWalkable returned the wrong changed flag.");
                map[x, z] = value;
                if ((edit & 3) == 3)
                {
                    grid.Rebuild();
                    AssertEquivalentToFresh(grid, map, chunkSize,
                        "random incremental " + sample + "/" + edit);
                }
            }
            grid.Rebuild();
            AssertEquivalentToFresh(grid, map, chunkSize, "random final " + sample);
        }
    }

    private static void CheckPattern(
        int width,
        int height,
        int chunkSize,
        Func<int, int, bool> predicate)
    {
        bool[,] map = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                map[x, z] = predicate(x, z);
            }
        }
        CheckMap(map, chunkSize);
    }

    private static void CheckMap(bool[,] map, int chunkSize)
    {
        ShadowConnectivityGrid grid = Build(map);
        ValidateAgainstOracle(grid, map, chunkSize, "pattern");
    }

    private static void AssertEquivalentAtAllLevels(
        ShadowConnectivityGrid grid,
        bool[,] map,
        string name)
    {
        foreach (int chunkSize in new[] { 8, 16, 32 })
        {
            ValidateAgainstOracle(grid, map, chunkSize,
                name + " chunk=" + chunkSize);
        }
    }

    private static void SeamContracts()
    {
        bool[,] map = new bool[33, 33];
        for (int x = 0; x < map.GetLength(0); x++)
        {
            map[x, 16] = true;
        }
        ShadowConnectivityGrid grid = Build(map);
        foreach (int seam in new[] { 7, 15 })
        {
            map[seam, 16] = false;
            Assert(grid.SetWalkable(seam, 16, false), "A seam split was ignored.");
            grid.Rebuild();
            AssertEquivalentAtAllLevels(grid, map, "seam split " + seam);
            map[seam, 16] = true;
            Assert(grid.SetWalkable(seam, 16, true), "A seam restore was ignored.");
            grid.Rebuild();
            AssertEquivalentAtAllLevels(grid, map, "seam restore " + seam);
        }
    }

    private static void PartialSuperChunkContracts()
    {
        bool[,] bridge = new bool[32, 16];
        for (int x = 0; x < 15; x++)
        {
            bridge[x, 0] = true;
        }
        bridge[16, 0] = true;
        ShadowConnectivityGrid bridgeGrid = Build(bridge);
        Assert(bridgeGrid.GetComponent(0, 0, 32) !=
            bridgeGrid.GetComponent(16, 0, 32),
            "A missing bridge was treated as connected across a partial superchunk.");
        bridge[15, 0] = true;
        Assert(bridgeGrid.SetWalkable(15, 0, true),
            "The partial-superchunk bridge edit was ignored.");
        bridgeGrid.Rebuild();
        Assert(bridgeGrid.GetComponent(0, 0, 32) ==
            bridgeGrid.GetComponent(16, 0, 32),
            "A bridge across a partial superchunk did not connect.");
        AssertEquivalentAtAllLevels(bridgeGrid, bridge,
            "partial-superchunk bridge");

        bool[,] alias = new bool[48, 32];
        alias[47, 0] = true;
        alias[47, 2] = true;
        alias[0, 16] = true;
        alias[0, 17] = true;
        alias[0, 18] = true;
        ShadowConnectivityGrid aliasGrid = Build(alias);
        Assert(aliasGrid.GetComponent(47, 0, 32) !=
            aliasGrid.GetComponent(47, 2, 32),
            "A partial-superchunk child aliased a component from the next row.");
        AssertEquivalentAtAllLevels(aliasGrid, alias,
            "partial-superchunk alias");

        bool[,] rotated = new bool[32, 48];
        rotated[0, 47] = true;
        rotated[2, 47] = true;
        rotated[16, 0] = true;
        rotated[17, 0] = true;
        rotated[18, 0] = true;
        ShadowConnectivityGrid rotatedGrid = Build(rotated);
        Assert(rotatedGrid.GetComponent(0, 47, 32) !=
            rotatedGrid.GetComponent(2, 47, 32),
            "A partial-superchunk child aliased a component from the next column.");
        AssertEquivalentAtAllLevels(rotatedGrid, rotated,
            "rotated partial-superchunk alias");
    }

    private static ShadowConnectivityGrid Build(bool[,] map)
    {
        int width = map.GetLength(0);
        int height = map.GetLength(1);
        ShadowConnectivityGrid grid = LoadMap(map);
        ShadowRebuildStats stats = grid.Rebuild();
        int leafColumns = (width + 7) / 8;
        int leafRows = (height + 7) / 8;
        int regionColumns = (leafColumns + 1) / 2;
        int regionRows = (leafRows + 1) / 2;
        int superChunkColumns = (regionColumns + 1) / 2;
        int superChunkRows = (regionRows + 1) / 2;
        Assert(stats.RebuiltLeaves == leafColumns * leafRows &&
            stats.RebuiltRegions == regionColumns * regionRows &&
            stats.RebuiltSuperChunks == superChunkColumns * superChunkRows,
            "The initial rebuild did not build every hierarchy node.");
        return grid;
    }

    private static ShadowConnectivityGrid LoadMap(bool[,] map)
    {
        int width = map.GetLength(0);
        int height = map.GetLength(1);
        ShadowConnectivityGrid grid = new ShadowConnectivityGrid(width, height);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (map[x, z])
                {
                    Assert(grid.SetWalkable(x, z, true),
                        "Initial walkable edit was not accepted.");
                }
            }
        }
        return grid;
    }

    private static void ScalarAllLevels(bool[,] map)
    {
        ScalarLabels(map, 8);
        ScalarLabels(map, 16);
        ScalarLabels(map, 32);
    }

    private static void AssertEquivalentToFresh(
        ShadowConnectivityGrid incremental,
        bool[,] map,
        int chunkSize,
        string name)
    {
        ShadowConnectivityGrid fresh = Build(map);
        foreach (int level in new[] { 8, 16, 32 })
        {
            ValidateAgainstOracle(incremental, map, level,
                name + " incremental chunk=" + level);
            ValidateAgainstOracle(fresh, map, level,
                name + " fresh chunk=" + level);
            ComparePartitions(incremental, fresh, map, level,
                name + " chunk=" + level);
        }
    }

    private static void ValidateAgainstOracle(
        ShadowConnectivityGrid grid,
        bool[,] map,
        int chunkSize,
        string name)
    {
        int[,] oracle = ScalarLabels(map, chunkSize);
        int width = map.GetLength(0);
        int height = map.GetLength(1);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                int actual = grid.GetComponent(x, z, chunkSize);
                Assert((actual == -1) == !map[x, z],
                    name + " blocked-cell label mismatch at " + x + "," + z + ".");
                for (int otherX = x; otherX < width; otherX++)
                {
                    int startZ = otherX == x ? z : 0;
                    for (int otherZ = startZ; otherZ < height; otherZ++)
                    {
                        if (x / chunkSize != otherX / chunkSize ||
                            z / chunkSize != otherZ / chunkSize)
                        {
                            continue;
                        }
                        bool expectedSame = oracle[x, z] == oracle[otherX, otherZ];
                        bool actualSame = actual ==
                            grid.GetComponent(otherX, otherZ, chunkSize);
                        Assert(expectedSame == actualSame,
                            name + " partition mismatch in chunk at " + x + "," + z +
                            " and " + otherX + "," + otherZ +
                            " (oracle=" + oracle[x, z] + "," + oracle[otherX, otherZ] +
                            ", actual=" + actual + "," +
                            grid.GetComponent(otherX, otherZ, chunkSize) + ").");
                    }
                }
            }
        }
    }

    private static void ComparePartitions(
        ShadowConnectivityGrid first,
        ShadowConnectivityGrid second,
        bool[,] map,
        int chunkSize,
        string name)
    {
        int width = map.GetLength(0);
        int height = map.GetLength(1);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                for (int otherX = x; otherX < width; otherX++)
                {
                    int startZ = otherX == x ? z : 0;
                    for (int otherZ = startZ; otherZ < height; otherZ++)
                    {
                        if (x / chunkSize != otherX / chunkSize ||
                            z / chunkSize != otherZ / chunkSize)
                        {
                            continue;
                        }
                        bool firstSame = first.GetComponent(x, z, chunkSize) ==
                            first.GetComponent(otherX, otherZ, chunkSize);
                        bool secondSame = second.GetComponent(x, z, chunkSize) ==
                            second.GetComponent(otherX, otherZ, chunkSize);
                        Assert(firstSame == secondSame,
                            name + " incremental/fresh partition mismatch.");
                    }
                }
            }
        }
    }

    private static void AssertBoundaryPartition(
        ShadowConnectivityGrid grid,
        bool[,] map,
        int chunkSize,
        string name)
    {
        int[,] oracle = ScalarLabels(map, chunkSize);
        int width = map.GetLength(0);
        int height = map.GetLength(1);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                bool boundary = x % chunkSize == 0 || z % chunkSize == 0 ||
                    x % chunkSize == chunkSize - 1 || z % chunkSize == chunkSize - 1;
                if (!boundary)
                {
                    continue;
                }
                for (int otherX = 0; otherX < width; otherX++)
                {
                    for (int otherZ = 0; otherZ < height; otherZ++)
                    {
                        bool otherBoundary = otherX % chunkSize == 0 ||
                            otherZ % chunkSize == 0 ||
                            otherX % chunkSize == chunkSize - 1 ||
                            otherZ % chunkSize == chunkSize - 1;
                        if (!otherBoundary || x / chunkSize != otherX / chunkSize ||
                            z / chunkSize != otherZ / chunkSize)
                        {
                            continue;
                        }
                        bool expected = oracle[x, z] == oracle[otherX, otherZ];
                        bool actual = grid.GetComponent(x, z, chunkSize) ==
                            grid.GetComponent(otherX, otherZ, chunkSize);
                        Assert(expected == actual, name + " changed a boundary partition.");
                    }
                }
            }
        }
    }

    private static int[,] ScalarLabels(bool[,] map, int chunkSize)
    {
        int width = map.GetLength(0);
        int height = map.GetLength(1);
        int[,] labels = new int[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                labels[x, z] = -1;
            }
        }

        int next = 0;
        for (int chunkX = 0; chunkX < width; chunkX += chunkSize)
        {
            for (int chunkZ = 0; chunkZ < height; chunkZ += chunkSize)
            {
                int maxX = Math.Min(width, chunkX + chunkSize);
                int maxZ = Math.Min(height, chunkZ + chunkSize);
                for (int x = chunkX; x < maxX; x++)
                {
                    for (int z = chunkZ; z < maxZ; z++)
                    {
                        if (!map[x, z] || labels[x, z] >= 0)
                        {
                            continue;
                        }
                        int label = next++;
                        Queue<Cell> queue = new Queue<Cell>();
                        labels[x, z] = label;
                        queue.Enqueue(new Cell(x, z));
                        while (queue.Count > 0)
                        {
                            Cell current = queue.Dequeue();
                            Visit(current.X - 1, current.Z, chunkX, chunkZ,
                                maxX, maxZ, map, labels, label, queue);
                            Visit(current.X + 1, current.Z, chunkX, chunkZ,
                                maxX, maxZ, map, labels, label, queue);
                            Visit(current.X, current.Z - 1, chunkX, chunkZ,
                                maxX, maxZ, map, labels, label, queue);
                            Visit(current.X, current.Z + 1, chunkX, chunkZ,
                                maxX, maxZ, map, labels, label, queue);
                        }
                    }
                }
            }
        }
        return labels;
    }

    private static void Visit(
        int x,
        int z,
        int minX,
        int minZ,
        int maxX,
        int maxZ,
        bool[,] map,
        int[,] labels,
        int label,
        Queue<Cell> queue)
    {
        if (x < minX || x >= maxX || z < minZ || z >= maxZ ||
            !map[x, z] || labels[x, z] >= 0)
        {
            return;
        }
        labels[x, z] = label;
        queue.Enqueue(new Cell(x, z));
    }

    private static bool[,] Filled(int width, int height, bool value)
    {
        bool[,] map = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                map[x, z] = value;
            }
        }
        return map;
    }

    private static bool[,] Clone(bool[,] source)
    {
        int width = source.GetLength(0);
        int height = source.GetLength(1);
        bool[,] result = new bool[width, height];
        Array.Copy(source, result, source.Length);
        return result;
    }

    private static void RunBenchmark()
    {
        const int width = 250;
        const int height = 250;
        bool[,] map = new bool[width, height];
        Random random = new Random(668);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                map[x, z] = random.Next(100) < 68;
            }
        }

        for (int warmup = 0; warmup < 2; warmup++)
        {
            ScalarAllLevels(map);
            LoadMap(map).Rebuild();
        }

        BenchmarkResult[] scalarSamples = MeasureSamples("scalar all levels", delegate
        {
            ScalarAllLevels(map);
        });
        BenchmarkResult[] fullSamples = MeasureSamples("bit hierarchy full", delegate
        {
            LoadMap(map).Rebuild();
        });

        Console.WriteLine("FixWorld pathfinding benchmark: 250x250, chunk=8");
        Console.WriteLine("Workloads: scalar all levels (8/16/32) and hierarchy full " +
            "include their computation; hierarchy full includes construction/loading. ");
        PrintBenchmarkSummary(scalarSamples);
        PrintBenchmarkSummary(fullSamples);

        int[,] fixedEdits = FixedEdits();
        int[,] dispersedEdits = DispersedEdits(width, height);
        BenchmarkIncremental("incremental fixed32", map, fixedEdits);
        BenchmarkIncremental("incremental dispersed32", map, dispersedEdits);
    }

    private static BenchmarkResult Measure(string name, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long allocatedBefore = ReadAllocatedBytes();
        long startedAt = Stopwatch.GetTimestamp();
        action();
        long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
        long allocatedAfter = ReadAllocatedBytes();
        return new BenchmarkResult(name, elapsedTicks,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2,
            allocatedBefore < 0 || allocatedAfter < 0
                ? -1
                : allocatedAfter - allocatedBefore);
    }

    private static BenchmarkResult[] MeasureSamples(string name, Action action)
    {
        BenchmarkResult[] samples = new BenchmarkResult[5];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = Measure(name, action);
        }
        return samples;
    }

    private static void PrintBenchmarkSummary(BenchmarkResult[] samples)
    {
        BenchmarkResult median = Median(samples, samples[0].Name);
        PrintBenchmark(median);
        long minimum = samples[0].Ticks;
        long maximum = samples[0].Ticks;
        for (int index = 1; index < samples.Length; index++)
        {
            minimum = Math.Min(minimum, samples[index].Ticks);
            maximum = Math.Max(maximum, samples[index].Ticks);
        }
        Console.WriteLine("  samples=" + samples.Length + ", range=" +
            (minimum * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) +
            ".." +
            (maximum * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) +
            " ms");
    }

    private static void PrintBenchmark(BenchmarkResult result)
    {
        double milliseconds = result.Ticks * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine(result.Name.PadRight(28) +
            milliseconds.ToString("F2", CultureInfo.InvariantCulture).PadLeft(9) +
            " ms, gen0=" + result.Gen0 + ", gen1=" + result.Gen1 +
            ", gen2=" + result.Gen2 + ", allocated=" + FormatBytes(result.Allocated));
    }

    private static void BenchmarkIncremental(
        string name,
        bool[,] original,
        int[,] edits)
    {
        ShadowConnectivityGrid grid = LoadMap(original);
        grid.Rebuild();
        bool[,] current = Clone(original);
        BenchmarkResult[] samples = new BenchmarkResult[5];
        ShadowRebuildStats lastStats = default(ShadowRebuildStats);
        int changed = 0;
        for (int sample = 0; sample < samples.Length; sample++)
        {
            samples[sample] = Measure(name, delegate
            {
                changed = 0;
                for (int index = 0; index < edits.GetLength(0); index++)
                {
                    int x = edits[index, 0];
                    int z = edits[index, 1];
                    bool value = !current[x, z];
                    if (grid.SetWalkable(x, z, value))
                    {
                        changed++;
                    }
                    current[x, z] = value;
                }
                lastStats = grid.Rebuild();
            });
        }

        BenchmarkResult summary = Median(samples, name);
        PrintBenchmark(summary);
        long minimum = samples[0].Ticks;
        long maximum = samples[0].Ticks;
        for (int index = 1; index < samples.Length; index++)
        {
            minimum = Math.Min(minimum, samples[index].Ticks);
            maximum = Math.Max(maximum, samples[index].Ticks);
        }
        Console.WriteLine("  samples=" + samples.Length + ", range=" +
            (minimum * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) +
            ".." +
            (maximum * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) +
            " ms");
        Console.WriteLine("  samples=5, changed=" + changed +
            ", rebuilt(leaves/regions/super)=" + lastStats.RebuiltLeaves + "/" +
            lastStats.RebuiltRegions + "/" + lastStats.RebuiltSuperChunks +
            ", changed(leaves/regions/super)=" + lastStats.ChangedLeaves + "/" +
            lastStats.ChangedRegions + "/" + lastStats.ChangedSuperChunks);
    }

    private static BenchmarkResult Median(BenchmarkResult[] samples, string name)
    {
        long[] ticks = new long[samples.Length];
        for (int index = 0; index < samples.Length; index++)
        {
            ticks[index] = samples[index].Ticks;
        }
        Array.Sort(ticks);
        BenchmarkResult median = samples[0];
        for (int index = 0; index < samples.Length; index++)
        {
            if (samples[index].Ticks == ticks[ticks.Length / 2])
            {
                median = samples[index];
                break;
            }
        }
        return new BenchmarkResult(name + " (median)", median.Ticks,
            median.Gen0, median.Gen1, median.Gen2, median.Allocated);
    }

    private static int[,] FixedEdits()
    {
        int[,] edits = new int[32, 2];
        for (int index = 0; index < edits.GetLength(0); index++)
        {
            edits[index, 0] = index & 31;
            edits[index, 1] = 0;
        }
        return edits;
    }

    private static int[,] DispersedEdits(int width, int height)
    {
        int[,] edits = new int[32, 2];
        for (int index = 0; index < edits.GetLength(0); index++)
        {
            edits[index, 0] = (index * 47 + 13) % width;
            edits[index, 1] = (index * 71 + 29) % height;
        }
        return edits;
    }

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

    private static long ReadAllocatedBytes()
    {
        return AllocatedBytesReader();
    }

    private static string FormatBytes(long bytes)
    {
        return bytes < 0
            ? "unsupported"
            : bytes.ToString("N0", CultureInfo.InvariantCulture) + " B";
    }

    private static void AssertStatsZero(ShadowRebuildStats stats, string message)
    {
        Assert(stats.RebuiltLeaves == 0 && stats.ChangedLeaves == 0 &&
            stats.RebuiltRegions == 0 && stats.ChangedRegions == 0 &&
            stats.RebuiltSuperChunks == 0 && stats.ChangedSuperChunks == 0, message);
    }

    private static void AssertStatsChangedZero(
        ShadowRebuildStats stats,
        string message)
    {
        Assert(stats.ChangedLeaves == 0 && stats.ChangedRegions == 0 &&
            stats.ChangedSuperChunks == 0, message);
    }

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        assertions++;
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(
            "Expected " + typeof(TException).Name + ".");
    }

    private readonly struct Cell
    {
        internal Cell(int x, int z)
        {
            X = x;
            Z = z;
        }

        internal int X { get; }
        internal int Z { get; }
    }

    private readonly struct BenchmarkResult
    {
        internal BenchmarkResult(string name, long ticks, int gen0, int gen1, int gen2)
            : this(name, ticks, gen0, gen1, gen2, -1L)
        {
        }

        internal BenchmarkResult(
            string name,
            long ticks,
            int gen0,
            int gen1,
            int gen2,
            long allocated)
        {
            Name = name;
            Ticks = ticks;
            Gen0 = gen0;
            Gen1 = gen1;
            Gen2 = gen2;
            Allocated = allocated;
        }

        internal string Name { get; }
        internal long Ticks { get; }
        internal int Gen0 { get; }
        internal int Gen1 { get; }
        internal int Gen2 { get; }
        internal long Allocated { get; }
    }
}
