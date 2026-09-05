# Pathfinding optimization

Parent: [Documentation index](README.md)

FixWorld currently changes one part of RimWorld pathfinding: duplicate work in
`ConnectivitySource.UpdateIncrementally`. RimWorld remains authoritative for
reachability and path search.

The former 8/16/32 shadow hierarchy is documented here as a rejected experiment.
Its code, runtime hooks, diagnostics, developer action and standalone contract
project were removed after it failed to eliminate work in a representative
modded game.

## Production behavior

### Connectivity update deduplication

RimWorld expands each dirty connectivity cell to a 3 by 3 neighborhood. Those
neighborhoods overlap heavily. FixWorld replaces the temporary
`HashSet<IntVec3>` membership path with a dense generation-stamped visit map and
still calls RimWorld's `ComputeCellConnectivity` once for every unique cell.

The Harmony transpiler requires the expected compiled IL shape. If the scratch
set access pattern cannot be identified exactly, the optional hook fails to
install instead of partially rewriting the method.

Measured candidate workload:

| Measurement | Result |
| --- | ---: |
| Connectivity updates | 1,614 |
| Dirty cells | 82,047 |
| Raw 3 by 3 visits | 738,423 |
| Unique cells | 142,133 |
| Duplicate visits removed | 596,290 (80.8%) |

Controlled runs from the same frozen save:

| Measurement | Baseline | Patched run 1 | Patched run 2 |
| --- | ---: | ---: | ---: |
| Observed ticks | 2,814 | 4,423 | 2,204 |
| Total tick time | 8.486 ms/tick | 5.829 ms/tick | 6.458 ms/tick |
| `MapPreTick` | 1.992 ms/tick | 0.346 ms/tick | 0.317 ms/tick |
| `PathFinderTick` | 1.741 ms/tick | 0.098 ms/tick | 0.087 ms/tick |
| `GatherData` | 1.664 ms/tick | 0.048 ms/tick | 0.067 ms/tick |
| `ConnectivitySource` | 1.624 ms/tick | 0.015 ms/tick | 0.027 ms/tick |

Patched run 2 reduced normalized `ConnectivitySource` time by 98.4% against the
recorded baseline. The two patched runs cost 0.549 and 0.541 microseconds per
unique expanded cell. Ordinary play and wall construction/removal completed
without a reported connectivity exception.

The profiler totals are inclusive and overlap. They must not be added as
independent costs, and total tick differences still contain workload variance.

### Retained instrumentation

FixWorld continues to measure:

- `PathFinderTick`, request creation, enqueueing and synchronous searches;
- `PathFinderMapData.GatherData` and each data source;
- scheduling, worker barriers and queue delay;
- `Reachability.CanReach` and the vanilla reachability cache;
- request origin, traversal mode, end mode, target kind and distance;
- repeated destinations through the bounded four-way history tracker;
- expanded connectivity visits and affected 8, 16 and 32-cell areas;
- TPS through the shared profiler infrastructure.

The 8/16/32 counters describe spatial locality only. They no longer maintain a
second connectivity graph.

## Rejected 8/16/32 super-grid experiment

### Hypothesis

The experiment tested whether a Boids-style hierarchy could reuse spatial work:

```text
8 by 8 bitboard leaves
  -> 16 by 16 component regions
  -> 32 by 32 super-chunks
  -> boundary portals
  -> global connectivity graph
```

A changed cell rebuilt its leaf and immediate neighbors. Parent summaries were
rebuilt only when a child summary changed. Queries mapped both endpoints into
the global component graph without per-query allocation.

### What worked

The standalone implementation was technically sound within its deliberately
narrow binary/cardinal model:

- 59,820,814 assertions passed against independent scalar oracles.
- Random edits, component splits/merges, partial map edges and routes leaving
  and re-entering a super-chunk were covered.
- Warm repeated queries allocated zero bytes in the standalone check.
- A 250 by 250 synthetic map built the hierarchy in a 2.69 ms median.
- 20,000 mixed queries took a 0.51 ms median.
- 32 localized edits took 0.13 ms and 32 dispersed edits took 0.29 ms after the
  global graph was added.

In game, one captured full build took 11.2 ms. A later normal-play interval
recorded 2,599 incremental observations taking 6.2 ms total, with zero observer
failures. The representation was cheap to maintain.

### Why it was rejected

Cheap maintenance was not enough. The hierarchy initially ran beside RimWorld
and therefore saved no work. An active consumer then replaced only the inner
region search for a narrow pawn-null `PassDoors` / `OnCell` profile while
preserving outer Harmony hooks, argument preparation, vanilla cache publication
and cleanup.

The decisive normal-play run recorded:

| Measurement | Result |
| --- | ---: |
| Simulation ticks | 60,333 |
| Candidate inner cache misses | 219 |
| Queries served by the hierarchy | 0 |
| Pending-cell fallbacks | 67 |
| Pending-rectangle fallbacks | 78 |
| Modified-region fallbacks | 74 |
| Consumer cost | 10.6 ms total |

"Vanilla Furniture Expanded - Architect" patches `Region.Allows` for
prisoner-proof doors. Treating patched region semantics as compatible would
require either proof for arbitrary transpilers or mod-specific adapters. The
first is not generally possible from Harmony metadata, and the second creates a
per-mod compatibility system FixWorld does not want to maintain.

The result was 219 attempts, zero displaced vanilla searches and no demonstrated
speedup. More runtime would not change that structural result. Keeping the grid
would therefore mean paying maintenance and compatibility complexity for no
production benefit.

### Decision

The entire experiment was removed from production:

- shadow connectivity grid and per-map observer;
- active and manual reachability consumers;
- Harmony callbacks feeding the hierarchy;
- hierarchy hotpaths, snapshots, counters and diagnostics UI;
- debug comparison action;
- standalone hierarchy contract project and benchmark;
- environment switches and compatibility guards.

Raw captures remain under `data/profiling/captures/pathfinding/`, including:

- `shadow-graph-baseline-20260905-020751`;
- `shadow-graph-normal-20260905-021006`;
- `shadow-graph-edits-20260905-021640`.

The experiment still produced useful conclusions:

1. RimWorld map invalidations are spatially local enough for cheap incremental
   structures.
2. A faster data structure is not an optimization until it replaces expensive
   production work at a meaningful hit rate.
3. Reachability semantics are a public mod compatibility surface in practice.
4. New pathfinding work should target a measured vanilla method directly and
   prove displaced work before building another general navigation layer.
5. The retained locality and demand counters are sufficient to select a smaller
   future experiment without keeping this hierarchy alive.

## Future experiment gate

No super-grid, reachability cache, portal search or path reuse is currently
planned for production. A future proposal must first identify a concrete vanilla
hotspot and define:

- the exact work it replaces;
- compatibility semantics and fallback ownership;
- expected hit rate from existing demand counters;
- independent correctness fixtures;
- end-to-end measurement including maintenance and publication;
- a removal condition when the real workload shows no benefit.

This keeps the useful research direction available without carrying dormant
runtime infrastructure.
