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

Invalidations were strongly local. The three measured sizes form a fixed spatial
hierarchy rather than competing as one exclusive chunk size. An 8 by 8 leaf
stores one 64-bit cell mask, four leaves form a 16 by 16 region, and four regions
form a 32 by 32 super-chunk. Parent levels store aggregated components, portals,
and generations rather than rebuilding a second copy of all child cells.

Most requests were short, so hierarchical routing must retain a cheap local
path for same-chunk and nearby destinations. The 96 requests beyond 64 cells
still provide a real sample for portal-graph and corridor experiments.

The bounded target-history tracker reported 106 repeats within 600 ticks, but
also 914 direct-map collisions. The repeat count is therefore only a lower
bound and cannot yet justify or reject path reuse. The next instrumented build
uses four-way set associativity and treats only replacement of four still-live
entries as a collision.

The request observer consumed 2.6 ms total and the spatial observer 41.1 ms
total. Combined, that is about 0.10% of the recorded 43,380.6 ms tick time. The
spatial observer added about 1.95% relative to the measured connectivity time,
so it is suitable for bounded experiments but should not remain permanently
enabled at this detail without sampling or a cheaper counter path.

### Connectivity-union candidate, controlled runs

Both runs with the generation-stamped dense visit map started from the same
frozen save and bound the optional patch successfully. Run 2 used a fresh
RimWorld process and subtracted the paused one-tick loading snapshot. The
explicit topology fixtures are still required before the patch is accepted.

| Measurement | Baseline run 2 | Patched run 1 | Patched run 2 |
| --- | ---: | ---: | ---: |
| Observed ticks | 2,814 | 4,423 | 2,204 |
| Total tick time | 8.486 ms/tick | 5.829 ms/tick | 6.458 ms/tick |
| `MapPreTick` | 1.992 ms/tick | 0.346 ms/tick | 0.317 ms/tick |
| `PathFinderTick` | 1.741 ms/tick | 0.098 ms/tick | 0.087 ms/tick |
| `PathFinderMapData.GatherData` | 1.664 ms/tick | 0.048 ms/tick | 0.067 ms/tick |
| `ConnectivitySource` | 1.624 ms/tick | 0.015 ms/tick | 0.027 ms/tick |
| `ConnectivitySource` total | 4,570.6 ms | 67.4 ms | 58.6 ms |

Run 1 contained 1,000 `ConnectivitySource` calls. Run 2 contained 517 calls,
663,741 raw expansion visits, and 108,410 unique expanded cells. Its 555,331
duplicate visits were 83.7% of the raw inner-loop candidates. The normalized
run-2 reductions against baseline were 23.9% for total tick time, 84.1% for
`MapPreTick`, 95.0% for `PathFinderTick`, 96.0% for `GatherData`, and 98.4% for
`ConnectivitySource`.

The two candidate runs cost 0.549 and 0.541 microseconds per unique expanded
cell respectively, a difference of about 1.5%. This stable unit cost matters
more than the different per-tick totals because run 2 performed substantially
more connectivity work per update. The window move may have affected timings;
its contribution was not isolated. `ConnectivitySource` retained its separate 1.3 ms
maximum and no relevant runtime exception was logged.

The same process then ran at higher game speeds. These are delta windows from
successive paused snapshots, so totals and call counts are isolated while the
cumulative maximum values are not:

| Measurement | 2x delta | 3x delta |
| --- | ---: | ---: |
| Observed ticks | 8,067 | 9,729 |
| Total tick time | 5.208 ms/tick | 3.039 ms/tick |
| `PathFinderTick` | 0.0248 ms/tick | 0.0090 ms/tick |
| `GatherData` | 0.0134 ms/tick | 0.0035 ms/tick |
| Connectivity calls | 1,205 | 328 |
| Connectivity total | 14.4 ms | 5.2 ms |
| Connectivity average | 0.0120 ms/call | 0.0159 ms/call |
| Raw expansion visits | 40,722 | 17,496 |
| Unique expanded cells | 23,846 | 8,564 |
| Duplicate visits | 16,876 (41.4%) | 8,932 (51.1%) |
| Path requests observed | 1,315 | 333 |
| Target-history collisions | 0 | 0 |

The 3x window occurred later in the colony and contained far less path demand,
so the two speed windows must not be treated as a direct throughput comparison.
They do show that the candidate remained stable under faster ticking. No matching
FixWorld, Harmony, index, null-reference, or root-level exception was present in
the run log. A cumulative 1,909.8 ms tick spike appeared during the speed tests,
while the cumulative connectivity maximum stayed at 1.1 ms; the spike therefore
was not inside the measured connectivity source.

### Wall and ordinary-play smoke checks

After run 2 the user walled off parts of a food store and continued ordinary
play. The first interval contained 1,529 connectivity updates taking 47.7 ms
and 87,462 unique expanded cells. The subsequent interval contained 427 updates
taking 9.9 ms and 16,027 unique expanded cells. No matching runtime exception
was logged. These intervals include unrelated simulation work and are not
isolated construction benchmarks. Explicit blocked/reopened route verification,
door cases, and the repeat-load lifecycle check remain open.

One request in the subsequent interval reported 30,000 simulation ticks between
`TickStart` and scheduling. The cause is unresolved. A wall-clock freeze alone
does not establish that many simulated ticks elapsed. Queue-delay data from that
point needs investigation; independently measured connectivity durations remain
available.

The candidate also removes a `HashSet.Clear()` inside every dirty-cell iteration,
on a set constructed with capacity 160,000. The measured gain combines removing
that clearing cost, changing membership representation, and deduplicating the
union. The 80.8% duplicate count is not a measured attribution of runtime savings.

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

Split the map into a fixed three-level hierarchy:

```text
32 by 32 super-chunk
  -> 4 x 16 by 16 regions
       -> 4 x 8 by 8 bitboard leaves
            -> 64 cells in one ulong
```

The 8 by 8 leaf is the cell-level storage and rebuild unit. A 16 by 16 region
aggregates four leaves. A 32 by 32 super-chunk aggregates four regions and 16
leaves. Benchmarks still decide whether every level earns its retained memory
and update cost, but they no longer require one size to own every operation.

Each chunk owns:

- passability masks by supported traversal class;
- local connected-component labels;
- boundary portals to its four or eight neighboring chunks;
- topology and cost generation counters;
- compact dirty state for changed cells and edges.

A changed tile marks its own chunk. A change at a boundary also marks the
affected neighboring edge. Most updates should then inspect one chunk and a
small, bounded neighbor set instead of the whole map.

Map coordinates identify their leaf and parents directly. A change inside a
leaf touches no neighboring leaf. A north-edge change considers only the north
neighbor, while a corner change considers at most the three adjacent leaves.
Changed component or portal summaries propagate upward. Unchanged summaries
stop propagation immediately. This is the concrete boids-style neighborhood
rule used by invalidation, not merely an analogy to spatial partitioning.

Within an 8 by 8 leaf, cardinal frontier expansion uses shifts by one and eight
plus edge masks. Cross-leaf expansion transfers only boundary bits to the
corresponding neighbor. Diagonal movement uses the same representation with its
corner-cutting semantics kept explicit and verified against RimWorld.

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

## Applicable chess-engine concepts

Several chess-engine techniques map cleanly to the spatial hierarchy when their
invariants are translated rather than their names copied:

- A transposition table becomes a bounded cache of reachability answers, portal
  routes, corridors, and path suffixes keyed by traversal inputs and referenced
  generations.
- A principal variation becomes the last successful portal route or chunk
  corridor. Compatible requests validate and try it before a new graph search.
- Move ordering prioritizes portals using geometric cost, cached routes, and
  measured successful exits. With a correct A-star cost model it changes search
  effort and tie-breaking, not reachability.
- Iterative deepening becomes iterative corridor widening: search the local leaf
  or region first, then admit neighboring regions and finally the super-chunk
  graph only when the bounded search cannot answer the request.
- Quiescence becomes a publication boundary, not a minimax search. Dirty leaves,
  portals, and parents settle before a new immutable spatial snapshot is made
  visible to queries.
- Zobrist-style incremental hashes may later recognize a topology that returns
  to an earlier state. Generation counters remain the primary validity rule;
  hashes cannot be the sole correctness guard because collisions exist.

Alpha-beta pruning has no direct role in shortest-path search because there is
no adversarial minimax tree. It is excluded unless a separate AI decision model
introduces that structure.

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
cannot suppress overlap between neighboring dirty cells. The experimental
transpiler preserves RimWorld's loop and connectivity computation but replaces
the scratch `HashSet<IntVec3>` with a dense generation-stamped cell map owned per
`ConnectivitySource`. Each update advances the generation, and each expanded
cell becomes one array lookup and comparison. There is no per-update allocation,
hash calculation, or full-array clear during normal operation.

The transpiler requires the exact expected `Clear` and `Add` IL pattern. If that
pattern changes, the optional hook rejects the patch and leaves the original
method active. Compare two frozen-save runs with the recorded baseline.

This patch is useful on its own and establishes an A/B harness for later spatial
work. It is not the endpoint of this track.

### Phase 2: Attribute demand

Record request count, batch size, traversal profile, target shape, and pawn
category at request creation. Use a bounded detailed capture when a category
needs a deeper think, movement, needs, or health breakdown.

The passive counters are now attached to the canonical `CreateRequest` overload,
which covers queued and immediate searches without probing every pawn tick. They
record fixed distributions for pawn category, traversal mode, end mode, target
kind, distance, spatial locality, and constraints. A bounded tracker records
repeated targets within 600 ticks. Connectivity updates additionally report raw
and unique 3 by 3 expansion work plus affected chunk counts for the three
hierarchy levels.
The real-save run above validates this passive attribution path. Its repeat
tracker remains deliberately excluded from reuse decisions until its collision
rate is reduced.

### Phase 3: Build the shadow spatial model

Maintain layered 8 by 8 passability masks and generation counters from observed
map events. Aggregate them through 16 by 16 regions and 32 by 32 super-chunks.
Build components without answering gameplay queries. Measure update cost,
memory, dirty-set size, neighbor visits, and upward propagation depth.

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
- The shadow model starts with 8 by 8 bitboard leaves, 16 by 16 regions, and
  32 by 32 super-chunks. Benchmarks decide which queries and summaries belong at
  each level.
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
