using System;

using FixWorld.Pathfinding;
using Verse;
using Verse.AI;

internal static class ComparisonContracts
{
    internal static void Run(Action<bool, string> assert)
    {
        TraverseParms baseline = Baseline();
        IntVec3 start = new IntVec3(1, 0, 1);
        LocalTargetInfo target = new LocalTargetInfo(new IntVec3(2, 0, 2));

        assert(ReachabilityComparisonPolicy.IsCandidate(
            start, target, PathEndMode.OnCell, baseline),
            "The baseline reachability request was rejected.");
        assert(!default(CellRect).Equals(CellRect.Empty),
            "The CellRect stub collapsed default and Empty values.");
        assert(ReachabilityComparisonPolicy.IsCandidate(
            new IntVec3(1, 4, 1),
            new LocalTargetInfo(new IntVec3(2, 7, 2)),
            PathEndMode.OnCell, baseline),
            "Valid x/z cells with nonzero y were rejected.");
        assert(!ReachabilityComparisonPolicy.IsCandidate(
            start, new LocalTargetInfo(start), PathEndMode.OnCell, baseline),
            "A same-cell request was accepted.");
        assert(!ReachabilityComparisonPolicy.IsCandidate(
            new IntVec3(-1, 0, 1), target, PathEndMode.OnCell, baseline),
            "An invalid start cell was accepted.");
        assert(!ReachabilityComparisonPolicy.IsCandidate(
            start, new LocalTargetInfo(IntVec3.Invalid), PathEndMode.OnCell,
            baseline),
            "An invalid target cell was accepted.");
        assert(!ReachabilityComparisonPolicy.IsCandidate(
            start, target, PathEndMode.Touch, baseline),
            "A non-OnCell end mode was accepted.");
        assert(!ReachabilityComparisonPolicy.IsCandidate(
            start, new LocalTargetInfo(new Thing()), PathEndMode.OnCell,
            baseline),
            "A thing target was accepted as a cell-only request.");

        TraverseParms variant = baseline;
        variant.pawn = new Pawn();
        AssertRejected(assert, start, target, variant, "pawn");
        variant = baseline;
        variant.mode = TraverseMode.NoPassDoors;
        AssertRejected(assert, start, target, variant, "traverse mode");
        variant = baseline;
        variant.maxDanger = Danger.Some;
        AssertRejected(assert, start, target, variant, "danger");
        variant = baseline;
        variant.canBashDoors = true;
        AssertRejected(assert, start, target, variant, "door bashing");
        variant = baseline;
        variant.canBashFences = true;
        AssertRejected(assert, start, target, variant, "fence bashing");
        variant = baseline;
        variant.alwaysUseAvoidGrid = true;
        AssertRejected(assert, start, target, variant, "avoid grid");
        variant = baseline;
        variant.fenceBlocked = true;
        AssertRejected(assert, start, target, variant, "fence blocking");
        variant = baseline;
        variant.avoidDarknessDanger = true;
        AssertRejected(assert, start, target, variant, "darkness avoidance");
        variant = baseline;
        variant.avoidFog = true;
        AssertRejected(assert, start, target, variant, "fog avoidance");
        variant = baseline;
        variant.targetBuildable = new CellRect(1, 1, 2, 2);
        AssertRejected(assert, start, target, variant, "target-buildable rectangle");

        variant = baseline;
        variant.avoidPersistentDanger = true;
        assert(ReachabilityComparisonPolicy.IsCandidate(
            start, target, PathEndMode.OnCell, variant),
            "Avoid-persistent-danger incorrectly rejected a pure binary candidate.");
    }

    private static TraverseParms Baseline()
    {
        return new TraverseParms
        {
            mode = TraverseMode.PassDoors,
            maxDanger = Danger.Deadly,
            targetBuildable = default(CellRect)
        };
    }

    private static void AssertRejected(
        Action<bool, string> assert,
        IntVec3 start,
        LocalTargetInfo target,
        TraverseParms parms,
        string name)
    {
        assert(!ReachabilityComparisonPolicy.IsCandidate(
            start, target, PathEndMode.OnCell, parms),
            "A request with " + name + " was accepted.");
    }
}
