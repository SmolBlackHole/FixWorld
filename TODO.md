# FixWorld TODO

Parent: [Project README](README.md)

FixWorld is under a **feature freeze**. It owns early runtime startup and the
orchestration of `PlayDataLoader.DoPlayLoad()`. New optimizations begin only
after the affected area is measured and its existing code has one clear owner.

This file contains only open work. Completed migrations belong in Git history,
benchmarks, and logs.

## Working rules

- Keep exactly one active loading path and remove replaced code in the same cutover.
- Put RimWorld, Harmony, Unity, and external tool calls behind one explicit owner.
- Put code in Shared only when it is an actual cross-assembly contract.
- Workers prepare pure data; Unity and Verse state is committed on the main thread.
- Verify every behavioral change with the full mod list and a typed benchmark.

## Verified baseline

- Doorstop, Loader, Runtime, and Mod have separate boot and attachment ownership.
- FixWorld orchestrates all 16 play-data stages and owns lifecycle, scheduling,
  and telemetry.
- Python starts RimWorld, waits, validates, and aggregates JSON written by the Runtime.
- Shared provides isolated caching, scheduling, profiling, and event primitives.
- DDS creation runs deferred and starts `texconv` only through the tool wrapper.
- Texture discovery is indexed once, warm textures load from per-mod BC7 packs,
  and preloader read-ahead visits each pack at most once.
- The old loose-file `dds-v1` cache is removed automatically after replacement
  pack builds through the shared migration cleanup.

## Active order

1. Publish a runtime diagnostics snapshot and one compact startup summary.
2. Validate BC7 color and alpha output on affected third-party textures.
3. Measure packed read-ahead on genuinely slow storage.
4. Add a small read-only diagnostics window over the same snapshot.
5. Take deeper ownership of remaining RimWorld operations one stage at a time.
6. Only then activate new worker or texture-format experiments.

## DDS texture cache

The implemented path uses one Runtime-owned service, a persistent pack store,
the shared scheduler, and the external-tool wrapper. Misses use source assets on
the current launch, then one low-priority background job per mod publishes an
atomic `.fwdp` pack after the main menu is ready.

The current 88-mod warm baseline is about 23 to 25 seconds overall, about 0.3
seconds for texture loading, and 0.1 seconds inside packed DDS loading. The
cache contains 10,460 hits in 62 packs. On the local NVMe, 256 MiB packed
read-ahead is neutral for total startup time. Rebuilding 8,250 missing entries
into 52 packs took 282 seconds in the background with one active converter.
Warm access timestamps are updated per pack at most every 12 hours, so ordinary
warm starts do not rewrite the complete index.

An automated comparison decoded 10,344 packed BC7 top mip levels and compared
them with their source PNG or JPEG. Mean luminance ratios remained approximately
1.000 with and without PNG gamma, sRGB, or ICC metadata. The reported darker
runtime appearance therefore needs a named affected texture and must focus on
Unity sampling, alpha, and generated mip levels rather than broad top-level
color conversion.

A 40-texture converter sample took 956 ms with normal GPU BC7, 674 ms with
quick GPU BC7, and 21.9 seconds on the CPU. Quick mode increased mean top-level
RGBA error from 0.291 to 0.456 values out of 255. Keep normal GPU quality until
the runtime appearance issue is resolved.

### Remaining cache work

- [ ] Throttle or pause background work from CPU, I/O, RAM, and TPS budgets.
- [ ] Expose background progress and remaining assets to UI, logs, and benchmarks.
- [ ] Capture a named darker texture in-game, then validate its Unity sampling,
      alpha, and generated mip levels before treating BC7 as final.
- [ ] Compare BC3, uncompressed DDS, and BC7 only where visual validation finds a
      real compatibility or quality tradeoff.
- [ ] Measure pack read-ahead on HDD and the affected slow NVMe with tiered budgets.
- [ ] Decide whether settings must be available before normal-mod attachment so
      the first pack plan uses the configured cache budget rather than 6 GiB.
- [ ] Evaluate OBST as a possible pack format with a sidecar index.

## Deferred main-thread work

The current fully warm 88-mod runs spend about 11 to 13 seconds in
`DeferredMainThreadWork`, while FixWorld scheduling and frame-yield overhead is
about 0.5 seconds. The queue records owner, calls, failures, runtime, and queue
delay. Static constructors and finalization operations are separate work items.
With the current mod list, Lunar takes about 3.2 to 3.4 seconds, of which
GeologicalLandforms initialization takes about 2.5 seconds and Lunar's Harmony
wrapper refresh about 0.8 seconds. `ThingDef.PostLoad` takes about 1.1 seconds.

- [ ] Determine dependencies and actual main-thread requirements for every expensive action.
- [ ] Prepare pure data off-thread and commit results on the main thread in original order.
- [ ] Fall back to the original sequential path safely or terminate the load explicitly on failure.
- [ ] Verify deterministic order and identical results across repeated runs.

## Remaining play-data ownership

FixWorld owns the order. In the following areas, stage adapters still delegate
most of the work to RimWorld.

### Mod and assembly boot

- [ ] Split `LoadModContent()` into assembly discovery, assembly load, and enqueued asset work.
- [ ] Measure `GetAllFilesForModPreserveOrder()` and assembly discovery per mod.
- [ ] Fully own `CreateModClasses()` and measure constructor and Harmony time.
- [ ] Preserve mod order and Harmony expectations at every cutover.

### XML and definitions

- [ ] Measure XML reading, patch application, and definition import separately.
- [ ] Analyze cross-references, reference resolution, and both implied-definition stages separately.
- [ ] Measure existing RimWorld parallelism during definition construction before adding FixWorld workers.
- [ ] Document reflection, static resolvers, and global registry mutation as main-thread boundaries.

### Finalization and lifecycle

- [ ] Define the LongEvent thread, synchronous events, scene changes, and exception lifecycle as a Runtime contract.
- [ ] Re-emit and verify `MainMenuReady` across menu, game, menu, and second-game transitions.
- [ ] Reproduce the reported colony-to-menu crash with DDS enabled and disabled.
      The captured stack currently points to MapModeFramework background cache
      work after teardown, so do not attribute it to DDS without an A/B result.
- [ ] Continue reducing RimWorld and Harmony calls to thin adapters over typed FixWorld work.

Acceptance for every stage cutover:

- [ ] Mod list and order remain identical; main menu, Quarry save, UI, telemetry,
      and benchmark work without relevant errors.

## Scheduling and workers

- [ ] Measure stage-specific parallelism, resource class, and worker count against CPU, memory, and storage.
- [ ] Capture RAM, VRAM, queue, GC, render-pause, and wall time per stage.
- [ ] Test RimWorld's Unity Job System with an isolated `IJob` and `NativeArray` prototype.
- [ ] Then decide which work belongs to Unity Jobs, FixWorld workers, or the main thread.

## Benchmarks and pilot operation

- [ ] Make preloader state explicit per benchmark instead of inheriting the installed state.
- [ ] Compare PNG/JPG, DDS, and DDS build with cold and warm OS caches. Measure
      `texconv` batch sizing separately from the scheduler worker count.
- [ ] Measure read-ahead on NVMe and HDD with tiered budgets, separating seek time and throughput.
- [ ] Preload mod files and assemblies with DDS under a byte budget and measure RAM and I/O peaks.

## Diagnostics, logging, and in-game UI

Goal: the Runtime owns one cheap diagnostics source. Loader and Mod expose the
data only at their boundaries. Opening the UI must not install patches or enable
profiling that was previously inactive.

- [ ] Compose one immutable, versioned runtime snapshot from the early timeline,
      stage telemetry, deferred work, scheduler, DDS, and memory snapshots.
- [ ] Feed benchmark JSON, compact log summary, and UI from that snapshot instead
      of maintaining three measurement paths.
- [ ] Separate always-on cheap counters from explicitly enabled detailed capture.
- [ ] Keep detail events in a bounded ring buffer and aggregate repeated issues by owner, path, and fingerprint.
- [ ] Log only boot milestones, contract errors, and fallbacks from the Loader.
- [ ] Name early-timeline fields precisely; observed early mod assemblies are not the active mod count.
- [ ] Write one compact Runtime summary at `MainMenuReady` with stage hotpaths,
      deferred hotpaths, DDS state, and worker utilization.
- [ ] Aggregate missing textures and NPOT warnings by mod and path; do not call
      them FixWorld errors without reliable attribution.
- [ ] Provide a normal-mod `MainButtonDef` and resizable diagnostics window while
      keeping Runtime and Shared free of Verse UI.
- [ ] Add Startup/Stages, Deferred/Mods, DDS/Workers, and Issues views.
- [ ] Refresh the window at most every 250 to 500 ms and only for a new snapshot version.
- [ ] Export a typed diagnostic report from RimWorld using the benchmark contract.

Acceptance:

- [ ] The last completed startup remains visible until the next run.
- [ ] Closed UI and default logging create no log flood and no measurable hotpath.
- [ ] The diagnostics window works in the main menu and in-game without changing loader or profiler state.

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
