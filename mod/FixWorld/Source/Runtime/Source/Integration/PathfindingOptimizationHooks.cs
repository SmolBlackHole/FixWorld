using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class PathfindingOptimizationHooks
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(ConnectivityUnionPatch)
        };

        [HarmonyPatch(
            typeof(ConnectivitySource),
            nameof(ConnectivitySource.UpdateIncrementally))]
        private static class ConnectivityUnionPatch
        {
            private static readonly ConditionalWeakTable<
                ConnectivitySource,
                VisitMap> VisitMaps = new();

            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions,
                ILGenerator generator)
            {
                FieldInfo vanillaCheckedCells = AccessTools.DeclaredField(
                    typeof(ConnectivitySource),
                    "checkedCells") ??
                    throw new MissingFieldException(
                        typeof(ConnectivitySource).FullName,
                        "checkedCells");
                MethodInfo clear = AccessTools.DeclaredMethod(
                    typeof(HashSet<IntVec3>),
                    nameof(HashSet<IntVec3>.Clear)) ??
                    throw new MissingMethodException(
                        typeof(HashSet<IntVec3>).FullName,
                        nameof(HashSet<IntVec3>.Clear));
                MethodInfo add = AccessTools.DeclaredMethod(
                    typeof(HashSet<IntVec3>),
                    nameof(HashSet<IntVec3>.Add)) ??
                    throw new MissingMethodException(
                        typeof(HashSet<IntVec3>).FullName,
                        nameof(HashSet<IntVec3>.Add));
                MethodInfo beginUpdate = AccessTools.DeclaredMethod(
                    typeof(ConnectivityUnionPatch),
                    nameof(BeginUpdate)) ??
                    throw new MissingMethodException(
                        typeof(ConnectivityUnionPatch).FullName,
                        nameof(BeginUpdate));
                MethodInfo tryVisit = AccessTools.DeclaredMethod(
                    typeof(ConnectivityUnionPatch),
                    nameof(TryVisit)) ??
                    throw new MissingMethodException(
                        typeof(ConnectivityUnionPatch).FullName,
                        nameof(TryVisit));

                var rewritten =
                    new List<CodeInstruction>(instructions);
                LocalBuilder visitMap = generator.DeclareLocal(
                    typeof(VisitMap));
                int clearIndex = -1;
                int addIndex = -1;
                int checkedCellLoads = 0;

                for (int index = 0; index < rewritten.Count; index++)
                {
                    CodeInstruction instruction = rewritten[index];
                    if (instruction.opcode == OpCodes.Ldsfld &&
                        Equals(instruction.operand, vanillaCheckedCells))
                    {
                        checkedCellLoads++;
                        if (index + 1 < rewritten.Count &&
                            rewritten[index + 1].Calls(clear))
                        {
                            if (clearIndex >= 0)
                            {
                                throw new InvalidOperationException(
                                    "ConnectivitySource clears its scratch " +
                                    "set more than once in the compiled " +
                                    "method.");
                            }

                            clearIndex = index;
                        }
                        else
                        {
                            instruction.opcode = OpCodes.Ldloc;
                            instruction.operand = visitMap;
                        }

                        continue;
                    }

                    if (!instruction.Calls(add))
                    {
                        continue;
                    }

                    if (addIndex >= 0)
                    {
                        throw new InvalidOperationException(
                            "ConnectivitySource adds to its scratch set " +
                            "more than once in the compiled method.");
                    }

                    addIndex = index;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = tryVisit;
                }

                if (clearIndex < 0 || addIndex < 0 || checkedCellLoads != 2)
                {
                    throw new InvalidOperationException(
                        "Could not identify RimWorld's connectivity scratch " +
                        "set access pattern.");
                }

                rewritten[clearIndex].opcode = OpCodes.Nop;
                rewritten[clearIndex].operand = null;
                rewritten[clearIndex + 1].opcode = OpCodes.Nop;
                rewritten[clearIndex + 1].operand = null;

                CodeInstruction first = rewritten[0];
                var loadInstance =
                    new CodeInstruction(OpCodes.Ldarg_0);
                loadInstance.labels.AddRange(first.labels);
                first.labels.Clear();
                rewritten.InsertRange(
                    0,
                    new[]
                    {
                        loadInstance,
                        new CodeInstruction(OpCodes.Call, beginUpdate),
                        new CodeInstruction(OpCodes.Stloc, visitMap)
                    });
                return rewritten;
            }

            private static VisitMap BeginUpdate(ConnectivitySource source)
            {
                VisitMap visitMap = VisitMaps.GetValue(
                    source,
                    CreateVisitMap);
                visitMap.BeginUpdate();
                return visitMap;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool TryVisit(VisitMap visitMap, IntVec3 cell) =>
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

            private sealed class VisitMap
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
}
