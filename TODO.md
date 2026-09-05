# FixWorld TODO

Parent: [Project README](README.md)

## Current rewrite

The original implementation is archived. The legacy checklist below describes
earlier work, not features currently active in the fork.
The approved [slice plan](docs/fork-refactor-plan.md) controls execution.

- [x] Bootstrap/installer/restart implementation: one early core and normal mod
      attachment, explicit lifecycle, owned installation and acknowledged helper.
- [ ] In-game bootstrap acceptance: remove the legacy installation explicitly,
      then test clean first boot, one restart, normal attach, Mods-tab restart
      and disabled ModConfig. Desktop process tests do not replace this.
      First install/restart/attach passed on 2026-09-05; Mods-tab restart and
      disabled entry still need native acceptance. See windows-preloader.md.
- [ ] Restore the archived loading UI and tips using the fork's shared
      measurements/contracts, without restoring the old custom mod loader.
      Implemented and loading screen checked natively, including the deferred
      background. Main-bar interaction/scrolling and the cross-reference guard
      still need native acceptance. See docs/diagnostics.md.
- [ ] Restore DDS after UI acceptance, using the fork's shared services and
      proven worker lifetime/maintenance behavior. No general scheduler rewrite
      is required ahead of it by the current approved continuation.
- [x] Local JSONL capture and Python collector: shared contracts, counter metadata,
      local tests and Release build. See [harness](docs/harness.md).
- [ ] In-game capture acceptance after deployment: manually launched game,
      JSONL and logs collected by Python, separate session after restart.

- [x] Slice 1: typed telemetry/profiling, real callers, focused tests,
      verification and commit. No lifecycle/cache/scheduler redesign.
- [x] Slice 2: shared typed caching and complete settings text-cache
      cutover. Exact scope and verification are in the slice plan.
- [x] News image ownership: preserve borrowed textures, release owned textures,
      isolate filenames by mod and invalidate queued loads on close/replacement.
      Contract tests and local build passed; in-game behavior remains unverified.
- [ ] Use the existing News/UpdateFeatureDef system for FixWorld changelog entries.

## Legacy checklist

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

The [binding module architecture](docs/runtime-modules.md) owns the approved
contracts, ownership rules, exclusions and acceptance criteria. Do not duplicate
or revise that architecture in this task list.

- [x] Audit runtime ownership and repeated telemetry wiring; record findings in
      the architecture document.
- [x] Phase 1: Shared base classes and their contracts only: service
      ownership, typed telemetry registration and common module lifecycle.
      No runtime wiring or diagnostics migration in this slice.
- [ ] Phase 2a (active): embed HugsLib as FixWorld.Foundation, independent of the
      research copy and original HugsLib. Bind initialization/logging/termination;
      test isolation and build, then in-game coexistence. See binding scope.
      - [x] Import isolated source/license, own project and package integration.
      - [x] Connect one Verse.Mod entry and fork lifecycle logging; preserve DDS.
      - [x] Compiled-assembly isolation checks and local/CI-reference builds.
      - [ ] In-game smoke: startup, existing DDS, quit, original HugsLib alongside.
- [ ] Phase 2b: move DDS into its owning module on the embedded foundation.
- [ ] Phase 2c: move Doorstop installation/detection/restart into FixWorldInstaller.
      Remove replaced ownership at each cutover; retain one runtime and restart.
- [ ] Phase 3: complete the Pathfinding module, common measurement and
      presentation contracts; remove replaced central field lists and forwarding.
- [ ] Phase 4: migrate remaining modules, including DDS and startup diagnostics,
      and remove their duplicated presentation and lifecycle wiring.
- [ ] Investigate code hot-reload after the module boundary is established,
      including in-flight work, state lifetime and the actual runtime mechanics.
- [ ] Validate Shared profiler recording and publication cost under Unity Mono.
      Use existing probes and logs; no repeated manual A/B restart choreography.

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
