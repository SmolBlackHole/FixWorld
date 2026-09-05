namespace FixWorld.Pathfinding
{
    internal enum ShadowQueryUnavailableReason
    {
        None,
        OutOfBounds,
        StartBlocked,
        TargetBlocked,
        MissingMapData,
        MissingFreshnessAccess,
        NotGathered,
        PendingCellDeltas,
        PendingRectDeltas,
        DirtyRegions,
        GridUnavailable,
        Exception,
        Count
    }
}
