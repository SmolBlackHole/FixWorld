# Pathfinding and spatial runtime optimization

Parent: [Documentation index](README.md)

This document owns FixWorld's pathfinding research and implementation direction.
The objective is not merely to make RimWorld's current methods a little faster.
The objective is to reduce how much spatial work is performed, how often it is
performed, and how often an answer must be computed again.

The target architecture is a direct, measured runtime optimization:

```text
map changes
  -> layered tile masks
  -> chunk-local components
  -> boundary portals
  -> global connectivity graph
  -> reachability
  -> hierarchical pathfinding
  -> path and corridor reuse
```

RimWorld's existing systems are integration points and a correctness oracle.
They are not a reason to discard a better representation.

## Principles

1. Reduce the candidate set before optimizing the inner loop.
2. Separate topology from traversal cost and temporary state.
3. Recompute only state invalidated by an observed map change.
4. Reuse connectivity, corridors, and paths while their dependencies remain
   valid.
5. Keep deterministic behavior and an explicit fallback for unsupported cases.
6. Run replacements in shadow mode against RimWorld before they affect play.
7. Require measured end-to-end gains, not only faster isolated operations.

This is the same broad idea as a spatial grid for boids. A query should inspect
the cells, chunks, or graph nodes that can affect the answer, not repeatedly
search the entire map.

## Measured baseline

Two runs of the same complex save identified a stable critical path:

| Measurement | Run 1 | Run 2 |
| --- | ---: | ---: |
| Observed ticks | 2,877 | 2,814 |
| Total tick time | 25,789.6 ms | 23,879.8 ms |
| `MapPreTick` | 5,706.2 ms | 5,604.0 ms |
| `PathFinderTick` | 4,877.6 ms | 4,899.6 ms |
| `PathFinderMapData.GatherData` | 4,623.1 ms | 4,682.9 ms |
| `ConnectivitySource` | not isolated | 4,570.6 ms |
| Path batches | 664 | 658 |
| Path requests | 862 | 861 |
| Grid jobs | 735 | 724 |
| Reachability-cache hit rate | 97.5% | 98.0% |

The profiler totals are inclusive and therefore overlap. They establish where
time is spent, but they must not be added together as independent costs.

The second run attributed about 97.6% of `GatherData` time to
`ConnectivitySource`. The Unity job barrier and request scheduling were small by
comparison. The first implementation experiment therefore targets connectivity
updates, while the longer research track changes the representation that makes
those updates and later searches expensive.

### Demand and invalidation sample

A 7,022-tick run of the same complex save on 2026-09-04 validated the request
and spatial counters:

| Measurement | Result |
| --- | ---: |
| Created path requests observed | 1,970 |
| Requests scheduled in path batches | 1,966 |
| Animal and wildlife requests | 1,323 (67.2%) |
| Mechanoid requests | 341 (17.3%) |
| Colonist requests | 271 (13.8%) |
| Requests at 0 to 16 cells | 1,560 (79.2%) |
| Requests beyond 64 cells | 96 (4.9%) |
| Connectivity updates | 1,614 |
| Dirty cells | 82,047 (50.8 per update) |
| Raw 3 by 3 expansion visits | 738,423 |
| Unique expanded cells | 142,133 (88.1 per update) |
| Affected 8 by 8 chunks | 8,340 (5.17 per update) |
| Affected 16 by 16 chunks | 5,886 (3.65 per update) |
| Affected 32 by 32 chunks | 4,428 (2.74 per update) |

The canonical `CreateRequest` counter was four requests above the queued batch
counter. This is consistent with the hook covering immediate or otherwise
non-queued requests without adding a pawn-tick probe.

The expanded connectivity rectangles contained 596,290 duplicate cell visits.
Deduplicating their union would remove 80.8% of the measured inner-loop visits
before accounting for the set construction itself. This is a direct reason to
run the Phase 1 patch rather than an estimate derived only from wall-clock time.

Invalidations were strongly local. A 16 by 16 chunk model touched 3.65 chunks
per update on average. It is the primary shadow-model baseline, with 8 by 8 and
32 by 32 retained as benchmark controls. The 32 by 32 model reduced the chunk
count by only 25% relative to 16 by 16 while making each full local rebuild four
times larger.

Most requests were short, so hierarchical routing must retain a cheap local
path for same-chunk and nearby destinations. The 96 requests beyond 64 cells
still provide a real sample for portal-graph and corridor experiments.

The bounded target-history tracker reported 106 repeats within 600 ticks, but
also 914 direct-map collisions. The repeat count is therefore only a lower
bound and cannot yet justify or reject path reuse. The tracker must be widened
or made set-associative before reuse rate is treated as a decision metric.

The request observer consumed 2.6 ms total and the spatial observer 41.1 ms
total. Combined, that is about 0.10% of the recorded 43,380.6 ms tick time. The
spatial observer added about 1.95% relative to the measured connectivity time,
so it is suitable for bounded experiments but should not remain permanently
enabled at this detail without sampling or a cheaper counter path.

## Current integration boundaries

RimWorld 1.6 already exposes useful points at which FixWorld can observe or
replace work:

- `PathFinderMapData` consumes dirty-cell events from map systems.
- `ConnectivitySource` publishes local neighbor connectivity for individual
  cells. It does not maintain the proposed global component graph.
- `MapGridRequest` groups compatible grid preparation within the current work
  batch.
- Reachability has a separate region and result cache.
- Map changes such as terrain, buildings, doors, fences, fog, areas, factions,
  danger, and water already provide invalidation signals.

FixWorld should reuse these signals where their semantics are sufficient. It can
replace the representation and algorithms behind them without taking ownership
of unrelated game systems.

## Layered map representation

The spatial model should store independent layers rather than repeatedly derive
one large per-cell object graph:

```text
stable topology
  structures
  terrain passability
  doors and fences
  water and traversal restrictions

dynamic restrictions
  temporary blockers
  reservations
  pawn areas
  faction and lord restrictions

dynamic costs
  terrain cost
  perceived danger
  darkness
  avoid grids
  custom path tuning
```

Each binary layer is a dense bitset or an equivalent contiguous value buffer.
Frequently traversed numeric costs use structure-of-arrays storage. Queries
compose only the layers required by their traversal profile.

Topology and cost invalidation must remain separate. A danger-cost change should
not rebuild connected components, and a reservation change should not invalidate
a path that does not depend on reservations.

At a 400 by 400 map size, one binary layer contains 160,000 bits, roughly
20 KiB before alignment. Several purpose-specific layers are therefore cheap
enough to keep resident if their update and composition costs are proven.

## Map tiling and local search

Split the map into fixed-size chunks. Candidate sizes such as 8 by 8, 16 by 16,
and 32 by 32 must be benchmarked rather than selected by intuition.

Each chunk owns:

- passability masks by supported traversal class;
- local connected-component labels;
- boundary portals to its four or eight neighboring chunks;
- topology and cost generation counters;
- compact dirty state for changed cells and edges.

A changed tile marks its own chunk. A change at a boundary also marks the
affected neighboring edge. Most updates should then inspect one chunk and a
small, bounded neighbor set instead of the whole map.

This is the boids-style reduction: first identify the spatial bucket, then
consider only nearby candidates that can change the result.

## Local components and the portal graph

Within a chunk, label connected passable cells for each supported traversal
class. Consecutive compatible cells along a chunk edge form a portal. The global
graph connects local components through those portals.

A graph node is conceptually:

```text
(map, chunk, local component, traversal class)
```

The graph does not need one node per map cell. A wall, door, or terrain change
causes the following update:

1. Update the relevant layer bits.
2. Rebuild local components in the affected chunk.
3. Recompute the affected boundary portals.
4. Update the global graph only if local or boundary connectivity changed.
5. Advance the generations of the state that actually changed.

Adding a blocker can split a component, while removing one can merge components.
The local rebuild handles both cases. Propagation beyond neighboring chunks is
needed only when the portal graph changes, not for every dirty cell.

Diagonal movement and corner-cutting rules are part of connectivity semantics.
They must be represented explicitly and tested against RimWorld's answer.

## Reachability

Reachability maps start and destination cells to local components, then queries
the connectivity graph. Its cache key includes every input that changes whether
the traversal is legal, not merely two cell indices.

At minimum, the key or its referenced profile covers:

- map and traversal class;
- `PathEndMode`;
- pawn, faction, lord, and allowed-area restrictions when applicable;
- destroyable and temporary blockers;
- path tuning or custom traversal logic;
- relevant topology generations.

Cached answers remain valid until one of their referenced generations changes.
FixWorld should compare every supported shadow query with RimWorld's original
reachability result before serving an answer to gameplay.

## Hierarchical pathfinding

The high-level search operates on local components and portals. It identifies a
small corridor of candidate chunks from the start component to the destination
component. A low-level search then expands cells only inside that corridor and
the small amount of permitted surrounding space needed for path quality.

```text
start cell
  -> start local component
  -> portal-graph route
  -> candidate chunk corridor
  -> cell-level path within that corridor
  -> destination cell
```

This reduces the search space without changing the final movement grid. Requests
with compatible traversal profiles or targets can share prepared graph and
corridor data. Unsupported path customizers use RimWorld's original pathfinder.

The implementation must record expanded cells, expanded graph nodes, path cost,
path length, worst-case latency, and fallback rate. A faster result that produces
materially worse paths is not an optimization.

## Path and corridor reuse

Exact `(start, destination)` memoization is too narrow because pawns often begin
on adjacent cells. Cache reusable routes at multiple levels:

- high-level portal routes;
- chunk corridors for a destination or destination region;
- complete paths when start and target inputs match;
- valid path suffixes for nearby followers;
- local flow or direction data only where request density justifies its cost.

A reusable entry records its traversal profile and the generations of chunks or
layers on which it depends. A change outside that dependency set does not evict
the entry. A change on the route invalidates it immediately or forces a cheap
validation before use.

This favors dependency-based invalidation over one global map generation, which
would discard unrelated work whenever any door or wall changes.

## Bit-parallel connectivity experiment

Before implementing graph repair machinery, benchmark a full connected-component
rebuild over dense bitsets. A frontier expansion can use shifted bitsets and
masking to evaluate many cells per operation:

```text
next = neighbors(frontier) & walkable & ~visited
```

Real maps span many machine words, include row boundaries, and require repeated
frontier iterations. The approach is not assumed to be constant-time. Measure it
against:

- RimWorld's current incremental connectivity update;
- a full scalar component rebuild;
- chunk-local scalar rebuilds;
- chunk-local bit-parallel rebuilds.

If a complete rebuild is already cheap at normal RimWorld map sizes, a lazy full
rebuild after topology changes may be simpler and faster than incremental graph
repair. Otherwise, use the same bit representation inside affected chunks.

## Pawn and request attribution

Animals and other pawn populations can multiply cheap work until it becomes a
runtime cost. FixWorld should first classify path request origins, because this
adds work only when a request is submitted rather than on every pawn tick:

```text
Colonists
Animals
Wildlife
Hostiles
Mechanoids
Other
```

Detailed pawn profiling should be a bounded capture mode, not a permanent set of
Harmony callbacks on every high-frequency method. A capture uses fixed slots and
allocation-free probes for categories such as movement, current job, think-tree
selection, needs, health, pathfinding, and reachability. The detailed hooks are
installed for a fixed tick window, aggregated off the recording path, then
removed.

Nested measurements form an inclusive call tree. They must not be presented as
additive self-time unless the instrumentation subtracts child time at the call
site.

## Implementation sequence

### Phase 1: Reduce the measured connectivity update

`ConnectivitySource.UpdateIncrementally` currently clears its scratch set for
each dirty cell before enumerating the surrounding 3 by 3 rectangle. That set
cannot suppress overlap between neighboring dirty cells. Test a direct patch
that deduplicates the union of expanded dirty cells using thread-local scratch
storage. Compare two frozen-save runs with the recorded baseline.

This patch is useful on its own and establishes an A/B harness for later spatial
work. It is not the endpoint of this track.

### Phase 2: Attribute demand

Record request count, batch size, traversal profile, target shape, and pawn
category at request creation. Use a bounded detailed capture when a category
needs a deeper think, movement, needs, or health breakdown.

The passive counters are now attached to the canonical `CreateRequest` overload,
which covers queued and immediate searches without probing every pawn tick. They
record fixed distributions for pawn category, traversal mode, end mode, target
kind, distance, and constraints. A bounded tracker records repeated targets
within 600 ticks. Connectivity updates additionally report raw and unique 3 by 3
expansion work plus affected chunk counts for the three candidate chunk sizes.
The real-save run above validates this passive attribution path. Its repeat
tracker remains deliberately excluded from reuse decisions until its collision
rate is reduced.

### Phase 3: Build the shadow spatial model

Maintain layered passability masks and generation counters from observed map
events. Build complete and chunk-local components without answering gameplay
queries. Measure update cost, memory, and dirty-set size.

### Phase 4: Add the portal graph

Build boundary portals and the global connectivity graph. Mirror reachability
queries, compare every result with RimWorld, and report mismatches by traversal
profile and invalidation cause.

### Phase 5: Reuse connectivity and routes

Serve proven reachability answers for supported profiles, then add generation-
validated portal routes, corridors, paths, and suffix reuse. Keep a per-feature
fallback rather than making the entire replacement all-or-nothing.

### Phase 6: Replace supported path searches

Run high-level graph search plus low-level corridor search for profiles that have
passed shadow validation. Continue to measure correctness, latency, path quality,
cache effectiveness, and fallback rate in the real game.

## Acceptance criteria

Each phase records:

- total and worst-case CPU time;
- managed allocations and retained memory;
- dirty cells, rebuilt chunks, changed portals, and graph nodes;
- request count, batch distribution, and traversal profiles;
- reachability mismatches against RimWorld;
- expanded cells and graph nodes;
- path cost and length differences;
- cache hit, validation, invalidation, and fallback counts.

Correctness fixtures must cover at least walls, doors, fences, water, destroyable
blockers, diagonal corners, temporary blockers, fog, pawn areas, faction and lord
restrictions, danger and darkness costs, custom path tuning, multiple maps, and
component splits and merges.

No replacement may affect gameplay until supported shadow queries produce zero
known mismatches across the frozen fixtures and representative mod packs.

## Decisions retained here

- The long-term target is a tiled spatial model, connectivity graph,
  hierarchical search, and path reuse. It is not limited to micro-optimizing the
  existing pathfinder.
- Direct patches of measured RimWorld methods are allowed.
- Topology, dynamic restrictions, and traversal costs have separate validity.
- Chunk size is selected by benchmark, not hard-coded by assumption.
- Detailed high-frequency profiling is sampled and temporary. Coarse profiler
  boundaries remain always on.
- RimWorld is the shadow-mode correctness oracle until a replacement is proven.
- Unsupported traversal customizers retain an explicit vanilla fallback.
- The first measured patch is connectivity duplicate suppression. The first
  architectural prototype is the shadow chunk and component model.

## Non-goals

- Rebuilding the mod loader.
- Serializing live RimWorld or mod objects.
- Applying broad RimThreaded-style semantic changes without a measured target.
- Hiding compatibility uncertainty behind a global enable switch.
