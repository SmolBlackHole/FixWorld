using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

using FixWorld.Pathfinding;

internal static class GlobalConnectivityContracts
{
    internal static void Run(Action<bool, string> assert)
    {
        InputContracts(assert);
        BoundaryGraphContracts(assert);
        EditContracts(assert);
        RandomContracts(assert);
    }

    internal static void RunBenchmark()
    {
        const int width = 250;
        const int height = 250;
        bool[,] map = new bool[width, height];
        var random = new Random(668);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                map[x, z] = random.Next(100) < 68;
            }
        }

        ShadowConnectivityGrid grid = Build(map);
        for (int warmup = 0; warmup < 2; warmup++)
        {
            RunQueries(grid, 2000);
        }

        long[] buildTicks = new long[3];
        for (int sample = 0; sample < buildTicks.Length; sample++)
        {
            long startedAt = Stopwatch.GetTimestamp();
            Build(map);
            buildTicks[sample] = Stopwatch.GetTimestamp() - startedAt;
        }

        long[] queryTicks = new long[3];
        long reachable = 0;
        for (int sample = 0; sample < queryTicks.Length; sample++)
        {
            long startedAt = Stopwatch.GetTimestamp();
            reachable = RunQueries(grid, 20000);
            queryTicks[sample] = Stopwatch.GetTimestamp() - startedAt;
        }

        Array.Sort(buildTicks);
        Array.Sort(queryTicks);
        Console.WriteLine("Global graph benchmark: 250x250, binary/cardinal");
        Console.WriteLine("  full grid + graph build (incl allocation and population) (median of 3)=" +
            Milliseconds(buildTicks[1]) + " ms, nodes=" + grid.GraphNodeCount +
            ", boundary-cell-links=" + grid.GraphEdgeCount);
        Console.WriteLine("  20,000 same/cross-super-chunk queries (median of 3)=" +
            Milliseconds(queryTicks[1]) + " ms, reachable=" + reachable);
    }

    private static long RunQueries(ShadowConnectivityGrid grid, int count)
    {
        long reachable = 0;
        int width = grid.Width;
        int height = grid.Height;
        for (int index = 0; index < count; index++)
        {
            int startX = (index * 47 + 11) % width;
            int startZ = (index * 71 + 13) % height;
            int targetX;
            int targetZ;
            if ((index & 1) == 0)
            {
                targetX = ((startX >> 5) << 5) + ((index * 19 + 3) & 31);
                targetZ = ((startZ >> 5) << 5) + ((index * 23 + 5) & 31);
                targetX = Math.Min(width - 1, targetX);
                targetZ = Math.Min(height - 1, targetZ);
            }
            else
            {
                targetX = (startX + 128) % width;
                targetZ = (startZ + 96) % height;
            }

            if (grid.AreConnected(startX, startZ, targetX, targetZ))
            {
                reachable++;
            }
        }
        return reachable;
    }

    private static string Milliseconds(long ticks)
    {
        return (ticks * 1000.0 / Stopwatch.Frequency).ToString(
            "F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void InputContracts(Action<bool, string> assert)
    {
        bool[,] map = new bool[3, 3];
        map[0, 0] = true;
        map[1, 1] = true;
        ShadowConnectivityGrid grid = Build(map);

        assert(!grid.AreConnected(0, 0, 1, 1),
            "Global connectivity allowed diagonal-only movement.");
        assert(grid.AreConnected(0, 0, 0, 0),
            "A walkable cell was not connected to itself.");
        assert(!grid.AreConnected(2, 2, 2, 2),
            "A blocked cell was connected to itself.");
        assert(!grid.AreConnected(0, 0, 2, 2),
            "A blocked endpoint was treated as globally reachable.");

        AssertThrows<ArgumentOutOfRangeException>(
            () => grid.AreConnected(-1, 0, 0, 0), assert);
        AssertThrows<ArgumentOutOfRangeException>(
            () => grid.AreConnected(0, 3, 0, 0), assert);
        AssertThrows<ArgumentOutOfRangeException>(
            () => grid.AreConnected(0, 0, 3, 0), assert);
        AssertThrows<ArgumentOutOfRangeException>(
            () => grid.AreConnected(0, 0, 0, -1), assert);

        map[1, 0] = true;
        assert(grid.SetWalkable(1, 0, true),
            "The pending global-query edit was ignored.");
        AssertThrows<InvalidOperationException>(
            () => grid.AreConnected(0, 0, 1, 1), assert);
        grid.Rebuild();
        assert(grid.AreConnected(0, 0, 1, 1),
            "A cardinal bridge was not reflected by global connectivity.");

        ShadowConnectivityGrid blocked = Build(new bool[33, 33]);
        assert(blocked.GraphRebuildCount == 1 && blocked.GraphNodeCount == 0 &&
            blocked.GraphEdgeCount == 0,
            "An initially blocked map did not publish an empty global graph.");
    }

    private static void BoundaryGraphContracts(Action<bool, string> assert)
    {
        bool[,] route = new bool[40, 8];
        for (int x = 2; x < 32; x++)
        {
            route[x, 1] = true;
        }
        for (int x = 8; x < 32; x++)
        {
            route[x, 6] = true;
        }
        for (int x = 32; x < 40; x++)
        {
            route[x, 1] = true;
            route[x, 6] = true;
        }
        for (int z = 1; z <= 6; z++)
        {
            route[32, z] = true;
        }

        ShadowConnectivityGrid grid = Build(route);
        int localStart = grid.GetComponent(2, 1, 32);
        int localTarget = grid.GetComponent(8, 6, 32);
        assert(localStart >= 0 && localTarget >= 0 && localStart != localTarget,
            "The leave-and-reenter fixture was not locally disconnected.");
        assert(ScalarCanReach(route, 2, 1, 8, 6),
            "The leave-and-reenter fixture was not connected by the scalar oracle.");
        assert(grid.AreConnected(2, 1, 8, 6),
            "The global graph did not join matching 32-cell boundary components.");
        assert(grid.AreConnected(8, 6, 2, 1),
            "Global connectivity was not symmetric.");
        assert(grid.GraphNodeCount == 3 && grid.GraphEdgeCount == 2,
            "The boundary graph did not publish expected nodes or boundary-cell links.");

        bool[,] pocket = new bool[32, 32];
        for (int x = 0; x < pocket.GetLength(0); x++)
        {
            pocket[x, 1] = true;
        }
        pocket[15, 15] = true;
        ShadowConnectivityGrid pocketGrid = Build(pocket);
        assert(!pocketGrid.AreConnected(0, 1, 15, 15),
            "An internally disconnected pocket was globally connected.");
        for (int z = 2; z < 15; z++)
        {
            pocket[15, z] = true;
            assert(pocketGrid.SetWalkable(15, z, true),
                "An internal pocket bridge edit was ignored at z=" + z + ".");
        }
        pocketGrid.Rebuild();
        assert(pocketGrid.AreConnected(0, 1, 15, 15),
            "Removing an internal pocket wall did not merge global connectivity.");
        for (int z = 2; z < 15; z++)
        {
            pocket[15, z] = false;
            assert(pocketGrid.SetWalkable(15, z, false),
                "An internal pocket split edit was ignored at z=" + z + ".");
        }
        pocketGrid.Rebuild();
        assert(!pocketGrid.AreConnected(0, 1, 15, 15),
            "Reintroducing an internal pocket wall did not split global connectivity.");

        bool[,] partial = new bool[33, 3];
        for (int x = 0; x < partial.GetLength(0); x++)
        {
            partial[x, 1] = true;
        }
        AssertMatchesOracle(Build(partial), partial, 80, 668, assert,
            "partial 33x3");

        bool[,] oneWide = new bool[1, 41];
        for (int z = 0; z < oneWide.GetLength(1); z++)
        {
            oneWide[0, z] = (z < 11) || (z >= 12 && z < 32);
        }
        AssertMatchesOracle(Build(oneWide), oneWide, 80, 669, assert,
            "one-wide");
    }

    private static void EditContracts(Action<bool, string> assert)
    {
        const int size = 65;
        bool[,] map = Filled(size, size, true);
        ShadowConnectivityGrid grid = Build(map);
        const int startX = 4;
        const int startZ = 10;
        const int targetX = 60;
        const int targetZ = 10;
        assert(grid.AreConnected(startX, startZ, targetX, targetZ),
            "The full map was not globally connected.");

        for (int z = 0; z < size; z++)
        {
            map[32, z] = false;
            assert(grid.SetWalkable(32, z, false),
                "A global split edit was ignored at z=" + z + ".");
        }
        AssertThrows<InvalidOperationException>(
            () => grid.AreConnected(startX, startZ, targetX, targetZ), assert);
        grid.Rebuild();
        assert(!grid.AreConnected(startX, startZ, targetX, targetZ),
            "A complete 32-cell wall did not split global connectivity.");

        map[32, 20] = true;
        assert(grid.SetWalkable(32, 20, true),
            "A global merge edit was ignored.");
        grid.Rebuild();
        assert(grid.AreConnected(startX, startZ, targetX, targetZ),
            "A one-cell opening did not merge global connectivity.");

        long graphRebuilds = grid.GraphRebuildCount;
        int graphNodes = grid.GraphNodeCount;
        int graphEdges = grid.GraphEdgeCount;
        ShadowRebuildStats noOp = grid.Rebuild();
        assert(noOp.RebuiltLeaves == 0 && noOp.RebuiltRegions == 0 &&
            noOp.RebuiltSuperChunks == 0,
            "A no-op global rebuild reported hierarchy work.");
        assert(grid.GraphRebuildCount == graphRebuilds &&
            grid.GraphNodeCount == graphNodes && grid.GraphEdgeCount == graphEdges,
            "A no-op global rebuild changed the published graph.");
        AssertRepeatedQuery(grid, startX, startZ, targetX, targetZ, assert);

        map[targetX, targetZ] = false;
        assert(grid.SetWalkable(targetX, targetZ, false),
            "A blocked endpoint edit was ignored.");
        grid.Rebuild();
        assert(!grid.AreConnected(targetX, targetZ, targetX, targetZ),
            "A blocked target was connected to itself after an edit.");
        assert(!grid.AreConnected(startX, startZ, targetX, targetZ),
            "A blocked target remained globally reachable.");
    }

    private static void RandomContracts(Action<bool, string> assert)
    {
        var random = new Random(668);
        for (int sample = 0; sample < 16; sample++)
        {
            int width = 1 + random.Next(65);
            int height = 1 + random.Next(65);
            bool[,] map = new bool[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    map[x, z] = random.Next(100) < 61;
                }
            }

            ShadowConnectivityGrid grid = Build(map);
            AssertMatchesOracle(grid, map, 90, random.Next(), assert,
                "random initial " + sample);
            for (int edit = 0; edit < 12; edit++)
            {
                int x = random.Next(width);
                int z = random.Next(height);
                bool value = random.Next(2) == 0;
                bool changed = grid.SetWalkable(x, z, value);
                bool expectedChanged = map[x, z] != value;
                assert(changed == expectedChanged,
                    "Global edit changed-state result was inconsistent.");
                map[x, z] = value;
                if (changed && (edit & 2) == 2)
                {
                    AssertThrows<InvalidOperationException>(
                        () => grid.AreConnected(0, 0, width - 1, height - 1),
                        assert);
                    grid.Rebuild();
                    AssertMatchesOracle(grid, map, 50, random.Next(), assert,
                        "random edit " + sample + "/" + edit);
                }
            }
            grid.Rebuild();
            AssertMatchesOracle(grid, map, 50, random.Next(), assert,
                "random final " + sample);
        }
    }

    private static void AssertMatchesOracle(
        ShadowConnectivityGrid grid,
        bool[,] map,
        int queryCount,
        int seed,
        Action<bool, string> assert,
        string name)
    {
        int width = map.GetLength(0);
        int height = map.GetLength(1);
        var random = new Random(seed);
        int totalQueries = Math.Min(queryCount, width * height * width * height);
        for (int index = 0; index < totalQueries; index++)
        {
            int startX = random.Next(width);
            int startZ = random.Next(height);
            int targetX = random.Next(width);
            int targetZ = random.Next(height);
            bool expected = ScalarCanReach(map, startX, startZ, targetX, targetZ);
            bool actual = grid.AreConnected(startX, startZ, targetX, targetZ);
            assert(expected == actual,
                name + " mismatch at " + startX + "," + startZ + " to " +
                targetX + "," + targetZ + " (expected=" + expected +
                ", actual=" + actual + ").");
        }
    }

    private static bool ScalarCanReach(
        bool[,] map,
        int startX,
        int startZ,
        int targetX,
        int targetZ)
    {
        if (!map[startX, startZ] || !map[targetX, targetZ])
        {
            return false;
        }
        if (startX == targetX && startZ == targetZ)
        {
            return true;
        }

        int width = map.GetLength(0);
        int height = map.GetLength(1);
        bool[,] visited = new bool[width, height];
        var queue = new Queue<Cell>();
        visited[startX, startZ] = true;
        queue.Enqueue(new Cell(startX, startZ));
        while (queue.Count > 0)
        {
            Cell current = queue.Dequeue();
            if (TryVisit(current.X - 1, current.Z, targetX, targetZ,
                map, visited, queue) ||
                TryVisit(current.X + 1, current.Z, targetX, targetZ,
                    map, visited, queue) ||
                TryVisit(current.X, current.Z - 1, targetX, targetZ,
                    map, visited, queue) ||
                TryVisit(current.X, current.Z + 1, targetX, targetZ,
                    map, visited, queue))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryVisit(
        int x,
        int z,
        int targetX,
        int targetZ,
        bool[,] map,
        bool[,] visited,
        Queue<Cell> queue)
    {
        if (x < 0 || x >= map.GetLength(0) || z < 0 ||
            z >= map.GetLength(1) || !map[x, z] || visited[x, z])
        {
            return false;
        }
        if (x == targetX && z == targetZ)
        {
            return true;
        }
        visited[x, z] = true;
        queue.Enqueue(new Cell(x, z));
        return false;
    }

    private static void AssertRepeatedQuery(
        ShadowConnectivityGrid grid,
        int startX,
        int startZ,
        int targetX,
        int targetZ,
        Action<bool, string> assert)
    {
        bool expected = grid.AreConnected(startX, startZ, targetX, targetZ);
        for (int index = 0; index < 5000; index++)
        {
            assert(grid.AreConnected(startX, startZ, targetX, targetZ) == expected,
                "Repeated global query changed its answer at iteration " + index + ".");
        }

        Func<long> allocatedBytes = CreateAllocatedBytesReader();
        long before = allocatedBytes();
        if (before < 0)
        {
            return;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        before = allocatedBytes();
        for (int index = 0; index < 5000; index++)
        {
            if (grid.AreConnected(startX, startZ, targetX, targetZ) != expected)
            {
                assert(false, "Warm repeated global query changed its answer.");
                return;
            }
        }
        long after = allocatedBytes();
        assert(after == before,
            "Warm repeated global queries allocated " + (after - before) + " bytes.");
    }

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

    private static ShadowConnectivityGrid Build(bool[,] map)
    {
        var grid = new ShadowConnectivityGrid(
            map.GetLength(0), map.GetLength(1));
        for (int x = 0; x < map.GetLength(0); x++)
        {
            for (int z = 0; z < map.GetLength(1); z++)
            {
                if (map[x, z])
                {
                    grid.SetWalkable(x, z, true);
                }
            }
        }
        grid.Rebuild();
        return grid;
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

    private static void AssertThrows<TException>(
        Action action,
        Action<bool, string> assert)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            assert(true, "Expected " + typeof(TException).Name + ".");
            return;
        }
        assert(false, "Expected " + typeof(TException).Name + ".");
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
}
