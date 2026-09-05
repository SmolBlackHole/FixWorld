# FixWorld TODO

Parent: [Project README](README.md)

FixWorld is under a **feature freeze**. RimWorld owns its original play-data
loader and deferred work list. FixWorld keeps passive stage diagnostics, exposes
that list through an isolated frame pump for the loading UI, and owns the DDS
texture optimization. Do not rebuild loader internals without a measured
bottleneck and a smaller replacement with a clear compatibility gain.

Implemented behavior belongs in the [documentation](docs/README.md), raw
measurements belong in `data/benchmarks`, and completed migrations belong in Git
history.

## Working rules

- Keep exactly one active loading path.
- Put RimWorld, Harmony, Unity, and external tool calls behind one explicit owner.
- Put code in Shared only when it is an actual cross-assembly contract.
- Prefer zero-copy handoffs and persistent caches when validity can be proven.
- Workers prepare pure data; Unity and Verse state stays on its required thread.
- Preserve effective mod order and RimWorld recovery behavior.
- Verify every behavioral change with a typed benchmark and a real game launch.

## Active order

1. Finish the semantics-preserving connectivity-union deduplication experiment.
   The frozen save measured 596,290 duplicate visits, 80.8% of the expanded
   3-by-3 dirty-cell work. Accept or reject the patch from two controlled runs.
2. Build and validate the 8-by-8 leaf, 16-by-16 region, and 32-by-32 super-chunk
   hierarchy in shadow mode. It must not answer gameplay queries yet.
3. Compare scalar, bit-parallel, full, and incremental component rebuild costs;
   promote no hierarchy work until its shadow answers match RimWorld.
4. Validate the shared profiler recording and publication cost under Unity Mono.
5. Capture a named darker texture and validate DDS sampling, alpha, and mipmaps.
6. Reduce and throttle DDS background work from measured resource pressure.
7. Re-run the second mod pack now that Royalty and Ideology are installed;
   verify the fixture's complete DLC set before treating the result as valid.

## Release engineering

- [ ] Verify the first automatic `pilot-N` GitHub prerelease from `main`, including
      the Windows ZIP, SHA-256 sidecar, and exact source commit.
- [ ] Protect `main` with the `Quality and package on Windows` status check after
      the renamed check has completed successfully once.
- [ ] Keep releases marked as pilot builds until the real-game checklist passes;
      a portable compile and package job is not a runtime stability claim.

## DDS texture cache

- [ ] Throttle or pause background conversion from CPU, I/O, RAM, and TPS budgets.
- [ ] Expose background progress and remaining assets to UI, logs, and benchmarks.
- [ ] Capture a named darker texture in-game, then validate Unity sampling, alpha,
      color space, and generated mip levels.
- [ ] Compare BC3, uncompressed DDS, and BC7 only where visual validation finds a
      real compatibility or quality tradeoff.
- [ ] Compare PNG/JPG, DDS reads, and DDS builds with cold and warm OS caches.
      Measure `texconv` batch sizing separately from scheduler worker count.
- [ ] Measure pack read-ahead on HDD and the affected slow NVMe with tiered budgets.
- [ ] Decide whether settings must be available before normal-Mod attachment so
      the first pack plan uses the configured cache budget rather than 6 GiB.
- [ ] Compare the packed store with an OBST container plus sidecar index only if
      current pack lookup or maintenance is measured as a problem.

## Runtime lifecycle and UI

- [ ] Verify `MainMenuReady` across menu, game, menu, and second-game transitions.
- [ ] Verify coordinated restart from the Mods tab and from a clean first
      Doorstop installation. Each path must close the old process before one
      replacement starts, and the replacement must show the FixWorld loading UI.
- [ ] Add measured worker utilization after the scheduler exposes busy and queued
      intervals.
- [ ] Aggregate missing textures and NPOT warnings by mod and path without calling
      them FixWorld errors unless attribution is reliable.
- [ ] Verify the diagnostics window in the main menu and in-game. Closed UI and
      default logging must have no measurable hotpath.
      Scroll preservation on refresh is implemented and compiled. Manually check
      live refresh, same-tab clicks, shrinking content, resize, and tab changes.

## Benchmark and pilot operation

- [ ] Make preloader state explicit per benchmark instead of inheriting installed
      state.
- [ ] Add a fixture capability check for required DLCs before launching RimWorld.

## In-game performance

- [x] Build a shared profiler with cached slots, raw timestamps, allocation-free
      scopes, inline and sharded aggregation, immutable published snapshots, and
      a Release benchmark harness.
- [ ] Validate profiler cost and snapshot publication inside Unity Mono. Desktop
      CLR measurements are not a substitute for the actual game runtime.
- [x] Surface profiler mode, publication age, and measured hotpaths in the
      diagnostics UI without formatting on the recording path.
- [x] Measure the frozen complex save twice and identify the dominant tick path.
- [ ] Separate `TickManager`, `MapPreTick`, `MapPostTick`, Unity Jobs, FixWorld
      workers, and main-thread time.
- [ ] Throttle background jobs from TPS, frame time, CPU pressure, and I/O pressure.
- [ ] Transfer RimThreaded patterns only to measured RimWorld 1.6 hotpaths.

### Pathfinding

Architecture, retained decisions, measurements, correctness cases, and the
ordered experiment design live in
[Pathfinding and spatial runtime optimization](docs/pathfinding.md).

#### Experiment discipline

- Keep one behavior-changing experiment active at a time. Instrumentation may
  ride along only when its measured overhead remains recorded separately.
- Use the same frozen save, game speed, warm-up point, sampling duration, mod
  list, DLC set, and background-work state for every comparison.
- Preserve the raw captures and compare medians plus worst cases across at least
  two runs. One faster launch is not evidence.
- A shadow model may observe and compare. It must not answer reachability or path
  queries until its own correctness and cost gate passes.
- Commit each accepted optimization independently. Rejected experiments keep a
  short result in the pathfinding document, not dormant runtime branches.

#### Experiment A: deduplicate vanilla connectivity work, active

Hypothesis: `ConnectivitySource.UpdateIncrementally` spends most of its measured
time recomputing cells already covered by another dirty cell's 3-by-3 expansion.
Replacing its per-update `HashSet<IntVec3>` membership work with a dense,
generation-stamped visit map should retain the exact union and call
`ComputeCellConnectivity` once per unique cell.

- [x] Instrument existing RimWorld 1.6 path jobs before replacing anything.
- [x] Record `PushRequest`, `FindPathNow`, queue latency, requests per tick, batch
      size, and grid-job creation.
- [x] Profile reachability and `ReachabilityCache` separately from pathfinding.
- [x] Measure the candidate workload: 82,047 dirty cells produced 738,423 raw
      3-by-3 visits but only 142,133 unique cells. The remaining 596,290 visits,
      or 80.8%, were duplicates.
- [x] Implement the optional generation-stamped dense deduplication patch with a
      strict IL-shape check and vanilla fallback if the target no longer matches.
- [x] Capture patched run 1 plus 2x and 3x stress windows. Run 1 reduced
      normalized `ConnectivitySource` time from 1.624 to 0.015 ms per tick;
      both speed windows remained stable and the four-way target tracker
      reported zero collisions.
- [x] Repeat the patched frozen save from the same on-disk checkpoint in a clean
      RimWorld process. Run 2 retained a stable 0.541 microseconds per unique
      expanded cell and reduced normalized connectivity time by 98.4% against
      the recorded baseline.
- [ ] Record, for baseline and patch, total tick time, `MapPreTick`,
      `PathFinderTick`, `GatherData`, `ConnectivitySource`, calls, average and
      maximum duration, raw and unique visits, allocations, and GC collections.
- [ ] Exercise topology changes that merge and split regions: construct and
      remove walls, open and close doors, and change passability while pawns are
      requesting paths.
      Wall construction and ordinary-play smoke intervals were logged without
      exceptions; explicit route outcomes and door cases remain unconfirmed.
- [ ] Investigate the isolated 30,000 simulation-tick queue-delay sample. Do not
      interpret it as elapsed wall time or a confirmed scheduling stall.
      Reproduce a debug clock jump with an outstanding request; distinguish
      requested-start age from actual enqueue latency.
- [ ] Strengthen the benchmark with A/B/A/B at equal simulated tick counts and
      matched save, camera, speed, warm-up, instrumentation, and background work.
      Add a bounded dirty-count breakdown (1, 2-4, 5-16, 17-64, 65+) only for this
      comparison to distinguish small-update overhead from large-union savings.
- [ ] Verify lifecycle behavior with colony to menu to the same colony while the
      profiler and DDS workers are active.
- [ ] Accept only if both runs show a stable end-to-end reduction, the unique
      visited-cell count remains correct, no path or reachability mismatch is
      observed, and the optional hook produces no runtime exception. Otherwise
      remove or disable the patch and document the rejected result.

The following passive counters may be validated from the same runs. They do not
open another optimization experiment:

- [x] Validate the new request-demand counters on the frozen save: pawn category,
      traversal and end mode, target shape, distance, constraints, and repeated
      destinations must cover queued and immediate requests without pawn-tick
      probes.
- [ ] Validate the four-way target-history tracker before using the repeated-
      destination rate for path-reuse decisions. The original direct-mapped
      tracker recorded 914 collisions in 1,970 requests, so its 106 repeats were
      only a lower bound.
- [ ] Validate the mutually exclusive same-leaf, same-region, same-super-chunk,
      and cross-super-chunk request counters on the frozen save.

#### Experiment B: shadow super-chunk hierarchy, active foundation

Hypothesis: map changes are spatially local even when their reachability effect
is not. A Boids-style neighborhood update can rebuild the directly affected
leaves and boundaries, while component and portal changes propagate through a
small parent hierarchy only when their summaries actually change.

Current slice: connect the validated cardinal, binary-passability hierarchy across
super-chunk boundaries and add observational global connectivity queries. Publish
completed updates under the per-map gate, test answers against a scalar whole-map
oracle, and measure bounded real-request probes in the existing profiler/UI/log.
No gameplay query replacement, pawn-specific traversal semantics, or path reuse.
Experiment A's remaining gameplay and queue-delay checks stay open.

- [ ] For future shadow-vs-game mismatches, retain a bounded reproducer with masks,
      endpoints, traversal profile, dirty cells, portal data, generations, and both
      answers. Keep local disconnection distinct from global unreachability;
      a route can leave and re-enter the endpoints' shared super-chunk.

- [x] Use measured unique expanded cells and 8, 16, and 32-cell chunk counts to
      select the first shadow-model representation and chunk-size candidates.
      Use 8 by 8 bitboard leaves, 16 by 16 regions, and 32 by 32 super-chunks.
- [x] Define the shadow data layout without Verse objects on the hot path:
      one `ulong` passability mask per 8-by-8 leaf, four leaves per 16-by-16
      region, and four regions per 32-by-32 super-chunk.
- [x] Define coordinate transforms and boundary masks once. Leaf, region, and
      super-chunk lookup should use shifts and masks rather than division in the
      measured path.
- [ ] Start with binary passability. Keep topology, traversal restrictions, and
      movement costs as separate layers with explicit generations instead of
      prematurely baking every traversal profile into one structure.
- [x] Implement Boids-style dirty-neighbor selection:
      an interior cell dirties only its leaf, an edge cell adds the adjacent
      leaf, and a corner adds at most three neighbors. Deduplicate the resulting
      leaf set before rebuilding.
- [x] Build a scalar reference implementation for local connected components and
      boundary portals. It is the oracle for the optimized leaf implementation.
- [ ] Build the bit-parallel 8-by-8 flood fill with shifts, edge masks, and one
      `ulong` frontier. Explicitly test cardinal, diagonal, map-edge, door, fence,
      water, temporary-blocker, and corner-cutting semantics.
      Cardinal binary flood fill and synthetic edges/splits/merges pass the
      independent scalar oracle. Actual RimWorld traversal semantics remain open.
- [x] Summarize each leaf's local components and exits. Rebuild a 16-by-16 parent
      only when a child summary changes, and a 32-by-32 super-chunk only when its
      region summary changes.
      Summary comparisons include perimeter occupancy as well as component IDs.
- [x] Publish topology coherently at an update barrier. Readers must observe the
      complete old generation or the complete new generation, never a mixture of
      partially rebuilt leaves and parents.
- [x] Join matching super-chunk boundary components into a global binary/cardinal
      graph. Handle component splits/merges and routes leaving/re-entering the
      endpoints' shared super-chunk. Query without per-request allocations.
- [x] Compare global binary answers with an independent whole-map scalar oracle
      across randomized edits and boundary regressions. This is not a comparison
      with RimWorld's pawn-specific reachability.
- [x] Run the global-query build in-game: observe answered/connected/unavailable
      counters and ShadowGridQuery timing during ordinary play and wall edits.
      Measure full/incremental costs again with the graph included. Query results
      remain observational and are not a pawn-specific correctness oracle.
      Normal play and mixed wall-edit intervals passed on 2026-09-05: zero
      observer failures and no unavailable probes. No live negative answer or
      gameplay speedup was demonstrated; see docs/pathfinding.md.
- [x] Feed RimWorld invalidation events into the shadow hierarchy, but keep
      vanilla connectivity and reachability authoritative.
      Per-map observer is wired after ComputeAll/UpdateIncrementally. Local-reference
      build and stubbed adapter tests pass. Live ordinary play and wall build/remove
      passed with zero observer failures; measurements are in docs/pathfinding.md.
- [ ] Run the live observer on the frozen colony: confirm initial full sampling,
      ordinary dirty updates, block/unblock changes, reload, and multiple maps.
      Capture Shadow grid counters and the three profiler slots with zero observer
      failures. Compare a FIXWORLD_SHADOW_GRID=0 control before claiming speedups.
- [ ] Compare every shadow reachability answer against RimWorld across empty and
      dense maps, disconnected rooms, one-cell corridors, bridges, doors, map
      edges, component merges, component splits, and multiple active maps.
- [ ] Benchmark scalar versus bit-parallel leaf rebuilds, full-map rebuilds versus
      incremental rebuilds, and all three hierarchy levels. Record wall time,
      allocations, memory, dirty and rebuilt leaves, neighbor visits, changed
      portals, propagation depth, worst case, and mismatch count.
      Standalone synthetic all-level/full/incremental timings are recorded in
      docs/pathfinding.md; isolated leaf timing and real-map costs remain open.
- [ ] Measure actual rebuild cost, not only touched-chunk counts. The existing
      8/16/32 counters describe locality but do not prove useful chunk sizes.
- [ ] Accept only with zero semantic mismatches and a meaningful measured saving
      after hierarchy maintenance and publication are included. Otherwise retain
      the measurements and simplify or reject the hierarchy before proceeding.

#### Later experiments, blocked on A and B

These are ordered candidates, not concurrent work. Start the next item only when
the preceding representation and generation rules have passed their gates.

- [ ] Add a fixed-window detailed capture for pawn movement, current jobs,
      think-tree selection, needs, health, pathfinding, and reachability. Use it
      to select the first consumer of the proven spatial model.
- [ ] Prototype a bounded semantic transposition table for reachability and
      high-level routes. Key entries by start component, target component,
      traversal profile, and relevant topology generations, not raw pawn or tile
      identity. Cache positive and negative answers separately.
- [ ] Define the table's validation and replacement policy before using it:
      prefer current generations, expensive or multi-region results, frequently
      reused entries, and stable traversal profiles. A hash collision must never
      be accepted as a correctness proof.
- [ ] Add `LastSuccessfulCorridor` or `PreferredCorridor` reuse. Validate every
      referenced leaf, portal, and generation; reuse it when valid and fall back
      cheaply when invalid. Do not expose chess-specific names in the runtime API.
- [ ] Add portal-history ordering only after corridor reuse is measurable. Track
      success count, recent success, remaining cost, and expansion savings; use
      these values only for candidate ordering and tie breaking, never to remove
      a legal path.
- [ ] Test the counter-move analogue: after entering through portal X, prefer
      exits that recently succeeded after X. Measure additional bookkeeping,
      expanded nodes, path cost, and fallback frequency.
- [ ] Test progressive corridor widening instead of chess-style iterative
      deepening: local leaf or region, preferred corridor plus one neighbor,
      plus two neighbors, then unrestricted graph search. Every failed narrow
      search must have a deterministic full fallback.
- [ ] Evaluate an optional per-leaf Zobrist-style fingerprint only after numeric
      generations are correct. Generations guard correctness; fingerprints may
      recognize a topology state returning to a previously cached identity.
- [ ] Let proven hierarchy data answer supported reachability queries in a narrow
      opt-in experiment with a vanilla fallback. Report hit rate, invalidations,
      mismatches, fallbacks, time saved, and memory before widening coverage.
- [ ] Test hierarchical portal-graph pathfinding only after shadow reachability
      and the narrow cache experiment pass. Compare expanded nodes, path cost and
      length, wall time, allocations, and worst-case regressions against vanilla.
- [ ] Add path and suffix reuse last, keyed by traversal semantics and all
      generations crossed by the reused segment. Invalidate locally where proven
      safe and fall back to a fresh vanilla path otherwise.

Explicit exclusions for this track:

- Do not implement Alpha-Beta, null-move pruning, late-move reductions, or any
  other pruning rule that can discard a legal route. Pathfinding has no minimax
  opponent and those techniques do not transfer safely.
- Do not use Zobrist hashes as the sole validity check.
- Do not combine a connectivity replacement, cache, new search, and path reuse in
  one benchmark. Their gains and correctness failures must remain attributable.

## Platform work, later

- [ ] Test RimWorld's Unity Job System with an isolated `IJob` and `NativeArray`
      prototype before shipping another scheduling runtime.
- [ ] Evaluate GPU decode, mipmaps, and uploads only after CPU ownership is clean.
- [ ] Build a Linux converter and explicit platform fallback.
