# FixWorld TODO

Parent: [Project README](README.md)

FixWorld is under a **feature freeze**. This file contains only concrete open
work. Implemented behavior belongs in the [documentation](docs/README.md), raw
measurements belong in `data/benchmarks`, and completed migrations belong in Git
history.

## Working rules

- Keep exactly one active loading path and remove replaced code in the same cutover.
- Put RimWorld, Harmony, Unity, and external tool calls behind one explicit owner.
- Put code in Shared only when it is an actual cross-assembly contract.
- Prefer zero-copy handoffs and persistent caches when validity can be proven.
- Workers prepare pure data; Unity and Verse state is committed on the main thread.
- Preserve effective mod order until result equivalence proves a narrower constraint.
- Verify every behavioral change with the full mod list and a typed benchmark.

Implemented boot, pipeline, cache, and diagnostics behavior is documented in
[Architecture](docs/architecture.md), the
[play-data pipeline](docs/play-data-pipeline.md),
[runtime diagnostics](docs/diagnostics.md), and the
[DDS cache](docs/dds-cache.md).

## Active order

1. Analyze and reduce the dominant deferred main-thread hotpaths.
2. Attribute patch-application cost by mod and operation type without changing
   patch order or ownership.
3. Split `ParseAndProcessXML()` measurements before considering replacement.
4. Take deeper ownership of remaining RimWorld operations one stage at a time.
5. Validate BC7 color and alpha output when a named affected texture is available.
6. Measure packed read-ahead when genuinely slow storage is available.

## Deferred main-thread work

- [ ] Trace inputs, outputs, global mutations, ordering dependencies, and actual
      main-thread requirements for each expensive action.
- [ ] Separate nested cost inside Lunar and Geological Landforms startup,
      `ThingDef.PostLoad`, static atlas baking, mod content reloads, and static
      constructors.
- [ ] Classify captured actions as main-thread-only, worker preparation with an
      ordered commit, or unknown. Do not make the general
      `ExecuteWhenFinished()` path concurrent.
- [ ] Prototype one explicitly whitelisted pure preparation path off-thread while
      preserving the original commit order.
- [ ] Define failure behavior: use the original sequential action safely or
      terminate the load explicitly instead of continuing with partial state.
- [ ] Verify deterministic order and identical produced data across repeated runs.

## Play-data ownership

### Mod and assembly boot

- [ ] Split `LoadModContent()` into assembly discovery, assembly loading, and
      enqueued asset work.
- [ ] Measure `GetAllFilesForModPreserveOrder()` and assembly discovery per mod.
- [ ] Fully own `CreateModClasses()` and attribute constructor and Harmony time.
- [ ] Use active dependency and load-order metadata as readiness constraints for
      pure preparation without weakening the effective total commit order.

### XML and definitions

- [ ] Attribute patch time by source mod, operation type, calls, and maximum
      duration while preserving the exact global patch sequence.
- [ ] Inventory custom `PatchOperation` types and side effects before deciding
      whether a patched-XML cache can be valid.
- [ ] Split `ParseAndProcessXML()` into inheritance registration, inheritance
      resolution, Def deserialization, and `ModContentPack` assignment.
- [ ] Analyze cross-references, reference resolution, and both implied-definition
      stages separately.
- [ ] Measure existing RimWorld parallelism during definition construction before
      adding FixWorld workers.
- [ ] Document reflection, static resolvers, and global registry mutation as
      explicit main-thread boundaries.

### Finalization and lifecycle

- [ ] Define the LongEvent thread, synchronous events, scene changes, and exception
      lifecycle as a Runtime contract.
- [ ] Re-emit and verify `MainMenuReady` across menu, game, menu, and second-game
      transitions.
- [ ] Reproduce the reported colony-to-menu crash with DDS enabled and disabled.
      The captured stack points to MapModeFramework background cache work after
      teardown, so do not attribute it to DDS without an A/B result.
- [ ] Continue reducing RimWorld and Harmony calls to thin adapters over typed
      FixWorld work.

Every stage cutover must preserve the active mod list and order and reach the
main menu and the Quarry test save with working UI, telemetry, and benchmark
output and no relevant errors.

## DDS texture cache

- [ ] Throttle or pause background work from CPU, I/O, RAM, and TPS budgets.
- [ ] Expose background progress and remaining assets to UI, logs, and benchmarks.
- [ ] Capture a named darker texture in-game, then validate Unity sampling, alpha,
      and generated mip levels.
- [ ] Compare BC3, uncompressed DDS, and BC7 only where visual validation finds a
      real compatibility or quality tradeoff.
- [ ] Measure pack read-ahead on HDD and the affected slow NVMe with tiered budgets.
- [ ] Decide whether settings must be available before normal-Mod attachment so
      the first pack plan uses the configured cache budget rather than 6 GiB.
- [ ] Evaluate OBST as a possible pack format with a sidecar index.

## Scheduling and workers

- [ ] Measure stage-specific parallelism, resource class, and worker count against
      CPU, memory, and storage.
- [ ] Capture RAM, VRAM, queue, GC, render-pause, and wall time per stage.
- [ ] Test RimWorld's Unity Job System with an isolated `IJob` and `NativeArray`
      prototype.
- [ ] Decide from those measurements which work belongs to Unity Jobs, FixWorld
      workers, or the main thread.

## Benchmarks and pilot operation

- [ ] Make preloader state explicit per benchmark instead of inheriting installed
      state.
- [ ] Compare PNG/JPG, DDS, and DDS build with cold and warm OS caches. Measure
      `texconv` batch sizing separately from scheduler worker count.
- [ ] Measure read-ahead on NVMe and HDD with tiered budgets, separating seek time
      and throughput.
- [ ] Preload mod files and assemblies alongside DDS under a byte budget and
      measure RAM and I/O peaks.

## Diagnostics, logging, and in-game UI

- [ ] Separate always-on cheap counters from explicitly enabled detailed capture.
- [ ] Add measured worker utilization after the scheduler exposes busy and queued
      intervals.
- [ ] Keep detail events in a bounded ring buffer and aggregate repeated issues by
      owner, path, and fingerprint.
- [ ] Log only boot milestones, contract errors, and fallbacks from the Loader.
- [ ] Name early-timeline fields precisely; observed early mod assemblies are not
      the active mod count.
- [ ] Aggregate missing textures and NPOT warnings by mod and path without calling
      them FixWorld errors unless attribution is reliable.
- [ ] Verify the diagnostics window in the main menu and in-game without changing
      loader or profiler state. Closed UI and default logging must have no
      measurable hotpath.

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
