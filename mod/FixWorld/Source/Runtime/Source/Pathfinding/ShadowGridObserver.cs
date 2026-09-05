using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FixWorld.Diagnostics;
using Verse;

namespace FixWorld.Pathfinding
{
    internal sealed class ShadowGridObserver
    {
        private const string EnabledEnvironmentVariable =
            "FIXWORLD_SHADOW_GRID";

        private readonly ConditionalWeakTable<Map, MapState> states =
            new();
        private static readonly
            ConditionalWeakTable<Map, MapState>.CreateValueCallback StateFactory =
                CreateState;

        internal static bool Enabled { get; } =
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariable) != "0";

        private readonly RuntimeTelemetryStore telemetry;

        internal ShadowGridObserver(RuntimeTelemetryStore telemetry)
        {
            this.telemetry = telemetry ??
                throw new ArgumentNullException(nameof(telemetry));
        }

        internal void Observe(
            Map map,
            List<IntVec3> deltas,
            bool fullRebuild)
        {
            if (!Enabled || map == null)
            {
                return;
            }

            RuntimeHotpath hotpath = fullRebuild
                ? RuntimeHotpath.ShadowGridFull
                : RuntimeHotpath.ShadowGridIncremental;
            long startedAt = Start(hotpath);
            MapState state = null;
            try
            {
                state = states.GetValue(map, StateFactory);

                // GatherData invokes source workers in parallel. This gate owns
                // the per-map source barrier, while different maps stay independent.
                lock (state.Sync)
                {
                    if (state.Disabled)
                    {
                        return;
                    }

                    try
                    {
                        bool initialize = state.Grid == null;
                        if (initialize)
                        {
                            state.Initialize(
                                map.cellIndices.SizeX,
                                map.cellIndices.SizeZ);
                        }

                        bool actualFullRebuild = fullRebuild || initialize;
                        if (actualFullRebuild)
                        {
                            hotpath = RuntimeHotpath.ShadowGridFull;
                        }

                        int sampledCells;
                        int changedCells;
                        if (actualFullRebuild)
                        {
                            SampleFull(
                                state,
                                map,
                                out sampledCells,
                                out changedCells);
                        }
                        else
                        {
                            SampleDeltas(
                                state,
                                map,
                                deltas,
                                out sampledCells,
                                out changedCells);
                        }

                        long rebuildStartedAt =
                            Start(RuntimeHotpath.ShadowGridRebuild);
                        ShadowRebuildStats stats;
                        try
                        {
                            stats = state.Grid.Rebuild();
                        }
                        finally
                        {
                            Stop(
                                RuntimeHotpath.ShadowGridRebuild,
                                rebuildStartedAt);
                        }

                        telemetry.ObserveShadowGrid(
                            actualFullRebuild,
                            sampledCells,
                            changedCells,
                            in stats);
                    }
                    catch (Exception exception)
                    {
                        DisableLocked(state, exception);
                    }
                }
            }
            catch (Exception exception)
            {
                Disable(state, exception);
            }
            finally
            {
                Stop(hotpath, startedAt);
            }
        }

        // This is deliberately a read-only, best-effort adapter. The update
        // callback owns the same per-map gate, so a successful query sees one
        // completed generation rather than a mixture of parent and child data.
        // Queries never initialize a map or read Verse map state.
        internal bool TryQuery(
            Map map,
            IntVec3 start,
            IntVec3 target,
            out bool connected,
            out long generation)
        {
            connected = false;
            generation = 0L;
            if (!Enabled || map == null || !states.TryGetValue(map, out MapState state))
            {
                return false;
            }

            if (!System.Threading.Monitor.TryEnter(state.Sync))
            {
                return false;
            }

            try
            {
                if (state.Disabled || state.Grid == null)
                {
                    return false;
                }

                if ((uint)start.x >= (uint)state.Width ||
                    (uint)start.z >= (uint)state.Height ||
                    (uint)target.x >= (uint)state.Width ||
                    (uint)target.z >= (uint)state.Height)
                {
                    return false;
                }

                ShadowConnectivityGrid grid = state.Grid;
                connected = grid.AreConnected(
                    start.x,
                    start.z,
                    target.x,
                    target.z);
                generation = grid.Generation;
                return true;
            }
            catch (Exception exception)
            {
                connected = false;
                generation = 0L;
                DisableLocked(state, exception);
                return false;
            }
            finally
            {
                System.Threading.Monitor.Exit(state.Sync);
            }
        }

        private static MapState CreateState(Map map) => new();

        private static void SampleFull(
            MapState state,
            Map map,
            out int sampledCells,
            out int changedCells)
        {
            sampledCells = 0;
            changedCells = 0;
            for (int z = 0; z < state.Height; z++)
            {
                for (int x = 0; x < state.Width; x++)
                {
                    bool walkable = GenGrid.WalkableByAny(
                        new IntVec3(x, 0, z),
                        map);
                    sampledCells++;
                    if (state.Grid.SetWalkable(x, z, walkable))
                    {
                        changedCells++;
                    }
                }
            }
        }

        private static void SampleDeltas(
            MapState state,
            Map map,
            List<IntVec3> deltas,
            out int sampledCells,
            out int changedCells)
        {
            sampledCells = 0;
            changedCells = 0;
            if (deltas == null)
            {
                return;
            }

            for (int index = 0; index < deltas.Count; index++)
            {
                IntVec3 cell = deltas[index];
                if ((uint)cell.x >= (uint)state.Width ||
                    (uint)cell.z >= (uint)state.Height)
                {
                    continue;
                }

                bool walkable = GenGrid.WalkableByAny(cell, map);
                sampledCells++;
                if (state.Grid.SetWalkable(cell.x, cell.z, walkable))
                {
                    changedCells++;
                }
            }
        }

        private void Disable(MapState state, Exception exception)
        {
            if (state == null)
            {
                return;
            }

            lock (state.Sync)
            {
                DisableLocked(state, exception);
            }
        }

        private void DisableLocked(MapState state, Exception exception)
        {
            if (state.Disabled)
            {
                return;
            }

            state.Disabled = true;

            try
            {
                telemetry.ObserveShadowGridFailure();
            }
            catch
            {
            }

            try
            {
                Log.Error(
                    "[FixWorld] Shadow grid observer disabled for one map " +
                    "after an exception: " + exception);
            }
            catch
            {
            }
        }

        private long Start(RuntimeHotpath hotpath)
        {
            try
            {
                return telemetry.StartRuntimeHotpath(hotpath);
            }
            catch
            {
                return long.MinValue;
            }
        }

        private void Stop(RuntimeHotpath hotpath, long startedAt)
        {
            if (startedAt == long.MinValue)
            {
                return;
            }

            try
            {
                telemetry.StopRuntimeHotpath(hotpath, startedAt);
            }
            catch
            {
            }
        }

        private sealed class MapState
        {
            internal readonly object Sync = new();

            internal ShadowConnectivityGrid Grid { get; private set; }

            internal int Width { get; private set; }

            internal int Height { get; private set; }

            internal bool Disabled { get; set; }

            internal void Initialize(int width, int height)
            {
                ShadowConnectivityGrid grid =
                    new ShadowConnectivityGrid(width, height);
                Width = width;
                Height = height;
                Grid = grid;
            }
        }
    }
}
