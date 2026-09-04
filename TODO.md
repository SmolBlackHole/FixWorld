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

1. Validate the shared profiler under Unity Mono and expose its published runtime
   snapshot through the existing diagnostics UI.
2. Measure the frozen complex save twice, then instrument the dominant tick path.
   Start with RimWorld's existing pathfinding jobs if the baseline confirms them.
3. Capture a named darker texture and validate DDS sampling, alpha, and mipmaps.
4. Reduce and throttle DDS background work from measured resource pressure.
5. Re-run the second mod pack now that Royalty and Ideology are installed;
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
- [ ] Surface profiler mode, publication age, and measured hotpaths in the
      diagnostics UI without formatting on the recording path.
- [ ] Measure the frozen complex save twice and identify the dominant tick path.
- [ ] Separate `TickManager`, `MapPreTick`, `MapPostTick`, Unity Jobs, FixWorld
      workers, and main-thread time.
- [ ] Throttle background jobs from TPS, frame time, CPU pressure, and I/O pressure.
- [ ] Transfer RimThreaded patterns only to measured RimWorld 1.6 hotpaths.

### Pathfinding

- [ ] Instrument existing RimWorld 1.6 path jobs before replacing anything.
- [ ] Record `PushRequest`, `FindPathNow`, queue latency, requests per tick, batch
      size, and `MapGridRequest` reuse.
- [ ] Separate `PathFinderMapData`, request context, traversal cost, and invalidation.
- [ ] Profile reachability and `ReachabilityCache` separately from pathfinding.
- [ ] Only then test path reuse and tiered path caches with precise invalidation.
- [ ] Report time, expanded nodes, path length, worst case, hit rate, and invalidations.

## Platform work, later

- [ ] Test RimWorld's Unity Job System with an isolated `IJob` and `NativeArray`
      prototype before shipping another scheduling runtime.
- [ ] Evaluate GPU decode, mipmaps, and uploads only after CPU ownership is clean.
- [ ] Build a Linux converter and explicit platform fallback.
