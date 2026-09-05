using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace FixWorld.Pathfinding
{
    internal static class ConnectivityUpdateDeduplication
    {
        private static readonly ConditionalWeakTable<
            ConnectivitySource,
            VisitMap> VisitMaps = new();

        internal static VisitMap BeginUpdate(ConnectivitySource source)
        {
            VisitMap visitMap = VisitMaps.GetValue(
                source,
                CreateVisitMap);
            visitMap.BeginUpdate();
            return visitMap;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryVisit(VisitMap visitMap, IntVec3 cell) =>
            visitMap.TryVisit(cell);

        private static VisitMap CreateVisitMap(
            ConnectivitySource source)
        {
            FieldInfo mapField = AccessTools.Field(
                typeof(SimplePathFinderDataSource<CellConnection>),
                "map") ??
                throw new MissingFieldException(
                    typeof(SimplePathFinderDataSource<CellConnection>)
                        .FullName,
                    "map");
            var map = (Map)mapField.GetValue(source);
            return new VisitMap(
                map.cellIndices.SizeX,
                map.cellIndices.NumGridCells);
        }

        internal sealed class VisitMap
        {
            private readonly int width;
            private readonly int[] generations;
            private int generation;

            internal VisitMap(int width, int cellCount)
            {
                this.width = width;
                generations = new int[cellCount];
            }

            internal void BeginUpdate()
            {
                if (generation == int.MaxValue)
                {
                    Array.Clear(generations, 0, generations.Length);
                    generation = 1;
                    return;
                }

                generation++;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool TryVisit(IntVec3 cell)
            {
                int index = (cell.z * width) + cell.x;
                if (generations[index] == generation)
                {
                    return false;
                }

                generations[index] = generation;
                return true;
            }
        }
    }
}
