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

1. Keep the measured connectivity deduplication, TPS measurement, and pathfinding
   diagnostics. The 8/16/32 hierarchy and active consumer were rejected after a
   60,333-tick run produced 219 attempts and zero served queries. RimWorld remains
   authoritative; retain the result as evidence instead of dormant runtime code.
2. Consolidate contracts, module facades, hooks, telemetry, and presentation in
   the ordered production-cleanup track below. Preserve behavior and probes.
3. Select the next optimization from measured hotspots after that cleanup;
   compare existing and new normal-play logs.
4. Validate the shared profiler recording and publication cost under Unity Mono.
5. Capture a named darker texture and validate DDS sampling, alpha, and mipmaps.
6. Reduce and throttle DDS background work from measured resource pressure.
7. Re-run the second mod pack now that Royalty and Ideology are installed;
   verify the fixture's complete DLC set before treating the result as valid.

## Production cleanup: contracts, hooks, and telemetry

Requested direction (2026-09-05): finish the current integration first, then
reduce repeated wiring and ownership ambiguity in small behavior-preserving
slices. This is not another loader replacement or a new profiling framework.
Retain the existing Shared profiler and its measurement capabilities.

Target ownership:

```text
RimWorld hooks -> small FixWorld facade / typed operation contract -> core logic
                              |
                     instrumented boundary
                              |
                  cached metric handles / module provider
                              |
                   read-only published diagnostic view
                         /                  \
                       UI                   log
```

Core logic and the instrumented facade implement/use the same operation
contracts. Profiling observes that boundary; it must not become the owner of
gameplay execution. Diagnostic views describe outcomes and measurements, not
mutable game state. Measure expensive internal operations where needed too,
with explicit inclusive/nested semantics instead of counting a call twice.

### Ordered implementation slices

- [ ] Inventory the current duplication and consumers before changing APIs.
      Start with `RuntimeDiagnosticsSummary`, `RuntimeTelemetryStore`,
      `RuntimeProfiling`, `RuntimeHost`, and `RuntimeContext`. Record ownership
      and repeated forwarding, metric definitions, snapshot copying, formatting,
      lifecycle setup, and hook registration. Keep useful profiling probes.
- [ ] Establish the Pathfinding module as the first complete vertical slice:
      thin Harmony/Verse adapters in the hook layer, a small FixWorld facade,
      explicit operation contracts, and module-owned core state. Move actual
      behavior out of profiling classes. The existing connectivity-deduplication
      helper belongs to this module too, not to the telemetry implementation.
- [ ] Separate RimWorld hook installation/adaptation from profiler attachment.
      Modules expose shared typed operation contracts; profiling attaches through
      a small instrumented facade or view using cached Shared profiler slots.
      Disabling measurement must not uninstall a functional optimization. Where
      measuring vanilla requires a Harmony probe, keep that probe in the adapter
      layer rather than introducing Harmony dependencies into the core contract.
- [ ] Define the small supported public FixWorld API deliberately. Keep private
      Harmony details, mutable services, and Verse implementation details out
      where consumers do not need them. Specify thread affinity, lifecycle,
      failure/fallback behavior, and view lifetime. Do not make every helper
      public or promise third-party compatibility for internal experiments.
- [ ] Let modules register their telemetry providers, metric descriptions, and
      read-only views explicitly at initialization. Return/cache typed recording
      handles from the existing Shared facilities. Specify unique identities,
      duplicate-registration behavior, reset, disposal, and disabled mode.
      Adding one module must not require another set of forwarding methods and
      field lists throughout the central store, host, and summary formatter.
- [ ] Reduce `RuntimeTelemetryStore` to the shared collection/publication and
      lifecycle responsibilities it actually owns. Put module-specific state,
      such as path-demand tracking, with its provider. Reuse aggregation and
      snapshot publication; avoid a second timer, counter, or event framework.
- [ ] Reduce `RuntimeDiagnosticsSummary` to presentation composition. Describe
      identity, label, unit, value, and grouping once where that removes current
      duplication, then render UI and logs from the same diagnostic view.
      Preserve useful domain-specific presentation rather than forcing every
      result into an untyped dictionary. Keep log keys/units stable or update
      parsers and documentation together; historical logs remain comparable.
- [ ] Consolidate the demonstrated repeated patterns into small interfaces,
      helpers, or an abstract base class where shared behavior warrants it.
      Contracts can expose an instrumented view/decorator without duplicating
      the implementation. Prefer composition when inheritance would join
      unrelated lifecycles. Remove replaced code in each slice, not after a
      permanent legacy adapter layer has accumulated.
- [ ] Apply the proven slice to the remaining runtime modules, then document the
      resulting ownership and public contracts in `docs/architecture.md` and
      `docs/diagnostics.md`. Keep this section as future work, not a claim that
      the target architecture already exists.

### Performance and acceptance constraints

- [ ] Registration and metadata resolution happen outside recording hotpaths.
      No per-probe reflection, string lookup, boxing, formatting, allocation,
      or synchronous subscriber fan-out. Use cached typed slots and retain
      separate worker/main-thread ownership instead of one global store lock.
- [ ] Prefer reusable buffers and zero-copy reads where ownership permits them.
      Readers must never observe a buffer being overwritten. Publish at a bounded
      cadence, reuse unchanged views, and copy only when snapshot lifetime or
      concurrency requires it. No tick-by-tick UI snapshot construction.
- [ ] Keep timing optional without disabling core logic. Test registration,
      reset/disposal across games, disabled profiling, published-view lifetime,
      concurrent recording where supported, and consistent UI/log values.
- [ ] Verify each slice with focused contracts and the existing Release profiling
      harness. Compare allocations and recording/publication cost, not shorter
      syntax as a speedup. Use the existing game probes and historical logs for
      normal-play assessment; do not require the user to repeat manual test
      choreography as a cleanup gate.
- [ ] Use [PerformanceOptimizer research](docs/research/performance-optimizer.md)
      as a reference for explicit optimization ownership and consumer-scoped
      work, not a template to copy wholesale. Keep FixWorld instrumentation and
      explicit cache invalidation. Abstractions should remove real repeated
      logic or decisions, not merely spread the same code over more files.

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
- [x] Report TPS as completed ticks over a roughly one-second wall-clock window;
      game-clock jumps do not inflate it and paused windows report zero.
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
- RimWorld owns reachability and path search. Do not revive the rejected shadow
  hierarchy without a newly measured consumer and explicit acceptance gate.
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
- [ ] Add a bounded dirty-count breakdown (1, 2-4, 5-16, 17-64, 65+) to the
      existing normal-play smoke log. A matched baseline remains optional future
      performance evidence, not a requirement for the current diagnostic slice.
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
- [ ] Validate the mutually exclusive 8/16/32-cell request-locality counters on
      the frozen save. These are passive measurements, not a connectivity grid.


#### Later runtime profiling

Start from a measured RimWorld hotspot rather than reviving the removed hierarchy.

- [ ] Add a fixed-window detailed capture for pawn movement, current jobs,
      think-tree selection, needs, health, pathfinding, and reachability. Use it
      to select the next directly replaceable hotspot.

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
