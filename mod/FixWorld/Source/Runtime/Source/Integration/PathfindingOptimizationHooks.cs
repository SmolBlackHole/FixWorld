using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using FixWorld.Pathfinding;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class PathfindingOptimizationHooks
    {
        internal static readonly Type[] PatchTypes =
        [
            typeof(ConnectivityUnionPatch)
        ];

        [HarmonyPatch(
            typeof(ConnectivitySource),
            nameof(ConnectivitySource.UpdateIncrementally))]
        private static class ConnectivityUnionPatch
        {
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
                    typeof(ConnectivityUpdateDeduplication),
                    nameof(ConnectivityUpdateDeduplication.BeginUpdate)) ??
                    throw new MissingMethodException(
                        typeof(ConnectivityUpdateDeduplication).FullName,
                        nameof(ConnectivityUpdateDeduplication.BeginUpdate));
                MethodInfo tryVisit = AccessTools.DeclaredMethod(
                    typeof(ConnectivityUpdateDeduplication),
                    nameof(ConnectivityUpdateDeduplication.TryVisit)) ??
                    throw new MissingMethodException(
                        typeof(ConnectivityUpdateDeduplication).FullName,
                        nameof(ConnectivityUpdateDeduplication.TryVisit));

                var rewritten =
                    new List<CodeInstruction>(instructions);
                LocalBuilder visitMap = generator.DeclareLocal(
                    typeof(ConnectivityUpdateDeduplication.VisitMap));
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
                    [
                        loadInstance,
                        new CodeInstruction(OpCodes.Call, beginUpdate),
                        new CodeInstruction(OpCodes.Stloc, visitMap)
                    ]);
                return rewritten;
            }
        }
    }
}
