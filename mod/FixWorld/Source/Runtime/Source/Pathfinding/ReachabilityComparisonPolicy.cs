using Verse;
using Verse.AI;

namespace FixWorld.Pathfinding
{
    internal static class ReachabilityComparisonPolicy
    {
        internal static bool IsCandidate(
            IntVec3 start,
            LocalTargetInfo target,
            PathEndMode endMode,
            TraverseParms parms) =>
            start.IsValid && target.IsValid && !target.HasThing &&
            start != target.Cell && endMode == PathEndMode.OnCell &&
            parms.pawn == null && parms.mode == TraverseMode.PassDoors &&
            parms.maxDanger == Danger.Deadly && !parms.canBashDoors &&
            !parms.canBashFences && !parms.alwaysUseAvoidGrid &&
            !parms.fenceBlocked && !parms.avoidDarknessDanger && !parms.avoidFog &&
            parms.targetBuildable.Equals(default(CellRect));
    }
}
