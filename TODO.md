# FixWorld TODO

Parent: [Project README](README.md)

FixWorld is under a **feature freeze**. RimWorld owns its original play-data
loader and deferred queue. FixWorld keeps passive stage diagnostics, the loading
UI, and the DDS texture optimization. Do not rebuild loader internals without a
measured bottleneck and a smaller replacement with a clear compatibility gain.

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

1. Reproduce the colony-to-menu crash with DDS enabled and disabled.
2. Fix DDS background completion after pack and index publication.
3. Capture a named darker texture and validate DDS sampling, alpha, and mipmaps.
4. Reduce and throttle DDS background work from measured resource pressure.
5. Re-run the second mod pack only when its required DLC set is available; its
   current recovery to official mods is not a valid performance result.

## DDS texture cache

- [ ] Reproduce the reported colony-to-menu crash with DDS enabled and disabled.
      The captured stack points to MapModeFramework background cache work after
      teardown, so do not attribute it to DDS without an A/B result.
- [ ] Throttle or pause background conversion from CPU, I/O, RAM, and TPS budgets.
- [ ] Fix DDS background completion: the pack files and index finish, but the
      benchmark completion report was still missing after 300 seconds.
- [ ] Expose background progress and remaining assets to UI, logs, and benchmarks.
- [ ] Capture a named darker texture in-game, then validate Unity sampling, alpha,
      color space, and generated mip levels.
- [ ] Compare BC3, uncompressed DDS, and BC7 only where visual validation finds a
      real compatibility or quality tradeoff.
- [ ] Measure pack read-ahead on HDD and the affected slow NVMe with tiered budgets.
- [ ] Decide whether settings must be available before normal-Mod attachment so
      the first pack plan uses the configured cache budget rather than 6 GiB.
- [ ] Compare the packed store with an OBST container plus sidecar index only if
      current pack lookup or maintenance is measured as a problem.

## Stage diagnostics and UI

- [ ] Verify all 17 passive boundaries across normal startup and RimWorld's
      recovery load. No stage hook may replace the original operation.
- [ ] Verify `MainMenuReady` across menu, game, menu, and second-game transitions.
- [ ] Separate always-on cheap counters from explicitly enabled detailed capture.
- [ ] Add measured worker utilization after the scheduler exposes busy and queued
      intervals.
- [ ] Name early-timeline fields precisely; observed early mod assemblies are not
      the active mod count.
- [ ] Aggregate missing textures and NPOT warnings by mod and path without calling
      them FixWorld errors unless attribution is reliable.
- [ ] Verify the diagnostics window in the main menu and in-game. Closed UI and
      default logging must have no measurable hotpath.

## Targeted loading experiments

- [ ] Attribute a slow stage by mod and operation only when the aggregate stage
      timing shows that the added instrumentation is justified.
- [ ] Compare PNG/JPG, DDS, and DDS build with cold and warm OS caches. Measure
      `texconv` batch sizing separately from scheduler worker count.
- [ ] Preload source files or assemblies alongside DDS only under an explicit byte
      budget and measure RAM, I/O, and critical-path overlap.
- [ ] Test RimWorld's Unity Job System with an isolated `IJob` and `NativeArray`
      prototype before shipping another scheduling runtime.

## Benchmark and pilot operation

- [ ] Make preloader state explicit per benchmark instead of inheriting installed
      state.
- [ ] Add a fixture capability check for required DLCs before launching RimWorld.
- [ ] Keep benchmark startup on monitor 2 by default and report the actual monitor.
- [ ] Measure read-ahead on NVMe and HDD with tiered budgets, separating seek time
      and throughput.

## In-game performance, later

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

- [ ] Evaluate GPU decode, mipmaps, and uploads only after CPU ownership is clean.
- [ ] Build a Linux converter and explicit platform fallback.
