# FixWorld TODO

Parent: [Project README](README.md)

FixWorld is under a **feature freeze**. RimWorld owns its original play-data
loader and deferred work list. FixWorld keeps passive stage diagnostics, pumps
that list across frames for a responsive loading UI, and owns the DDS texture
optimization. Do not rebuild loader internals without a measured bottleneck and
a smaller replacement with a clear compatibility gain.

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
- [ ] Compare PNG/JPG, DDS reads, and DDS builds with cold and warm OS caches.
      Measure `texconv` batch sizing separately from scheduler worker count.
- [ ] Measure pack read-ahead on HDD and the affected slow NVMe with tiered budgets.
- [ ] Decide whether settings must be available before normal-Mod attachment so
      the first pack plan uses the configured cache budget rather than 6 GiB.
- [ ] Compare the packed store with an OBST container plus sidecar index only if
      current pack lookup or maintenance is measured as a problem.

## Stage diagnostics and UI

- [ ] Verify all 17 passive boundaries across normal startup and RimWorld's
      recovery load. No stage hook may skip or reorder the original operation.
- [ ] Verify the deferred frame pump across normal startup, recovery, and nested
      `ExecuteWhenFinished()` registration. RimWorld's list and action order must
      remain authoritative.
- [ ] Verify `MainMenuReady` across menu, game, menu, and second-game transitions.
- [ ] Add measured worker utilization after the scheduler exposes busy and queued
      intervals.
- [ ] Aggregate missing textures and NPOT warnings by mod and path without calling
      them FixWorld errors unless attribution is reliable.
- [ ] Verify the diagnostics window in the main menu and in-game. Closed UI and
      default logging must have no measurable hotpath.

## Direct loader replacement research track

Build this as a local research prototype. It may patch or replace RimWorld
methods directly, load additional replacement assemblies, or rebuild selected
DLL boundaries when that gives us the cleanest test. Packaging and public
distribution are separate decisions and do not constrain the experiment. Keep
the prototype isolated from the active loader until it reproduces RimWorld's
observable result for the supported build.

- [ ] Record original assembly hashes and make the experimental installation
      reversible, then identify and patch the smallest exact RimWorld entry point.
- [ ] Build whatever FixWorld-owned patch or replacement DLLs are needed to take
      control at that boundary instead of preserving an artificial mod-only limit.
- [ ] Build a persistent mod index from `ModsConfig.xml`, effective load folders,
      dependency metadata, and discovered files. Reuse it on a matching start and
      reconcile additions, removals, and changed files during discovery.
- [ ] Discover assemblies once, construct their dependency and load-order graph,
      then load them deterministically. Preserve the active mod order wherever no
      stronger assembly dependency exists.
- [ ] Read XML files concurrently, parse independent documents in workers, merge
      deterministically, and execute patch operations strictly in effective mod
      order. Unknown or stateful custom patch operations must remain ordered and
      observable.
- [ ] Produce a per-mod texture plan while indexing so the complete required set
      is known early. Parallelize bounded file I/O and source decoding without
      touching Unity objects from workers.
- [ ] Preload indexed source files and assemblies only under an explicit byte
      budget. Measure RAM, I/O pressure, and overlap with the critical path.
- [ ] Load compatible DDS/BCn payloads directly. Build missing artifacts per mod
      and maintain a per-mod index, but compare a writable sidecar cache with the
      central cache before choosing storage. Never mutate Workshop or source mod
      directories implicitly.
- [ ] Evaluate `UnityWebRequestTexture` and `DownloadHandlerTexture` for supported
      source formats. Treat direct DDS/BCn upload as a separate path and commit
      texture creation and GPU upload through a bounded main-thread queue.
- [ ] Validate exact active-mod order, generated-at-start content, Def results,
      patch side effects, texture appearance, recovery behavior, and unsupported
      build fallback against the current RimWorld-owned baseline.
- [ ] Keep the replacement only if cold and warm benchmarks show a material gain
      on both the 88-mod fixture and the second mod pack. Otherwise retain the
      index or texture artifacts that independently prove useful and delete the
      replacement pipeline.

## Benchmark and pilot operation

- [ ] Make preloader state explicit per benchmark instead of inheriting installed
      state.
- [ ] Add a fixture capability check for required DLCs before launching RimWorld.

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

- [ ] Test RimWorld's Unity Job System with an isolated `IJob` and `NativeArray`
      prototype before shipping another scheduling runtime.
- [ ] Evaluate GPU decode, mipmaps, and uploads only after CPU ownership is clean.
- [ ] Build a Linux converter and explicit platform fallback.
