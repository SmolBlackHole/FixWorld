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

1. [x] Remove the generic `DefDatabase<ThingCategoryDef>` Harmony hook that caused
   100 `ThingDef.ResolveIcon()` failures. The replacement boundary passed with all
   90 active mods, both with DDS enabled and disabled.
2. Reproduce the colony-to-menu crash with DDS enabled and disabled.
3. [x] Fix DDS background completion after pack and index publication. A normal
   90-mod cold-cache run published 10,468 entries with 0 failures and emitted
   the benchmark completion report after 343.7 seconds of background work.
4. Capture a named darker texture and validate DDS sampling, alpha, and mipmaps.
5. Reduce and throttle DDS background work from measured resource pressure.
6. Re-run the second mod pack now that Royalty and Ideology are installed;
   verify the fixture's complete DLC set before treating the result as valid.

## Release engineering

- [ ] Verify the first automatic `pilot-N` GitHub prerelease from `main`, including
      the Windows ZIP, SHA-256 sidecar, and exact source commit.
- [ ] Protect `main` with the `Quality and package on Windows` status check after
      the renamed check has completed successfully once.
- [ ] Keep releases marked as pilot builds until the real-game checklist passes;
      a portable compile and package job is not a runtime stability claim.

## DDS texture cache

- [x] A/B the 100 `ThingDef.ResolveIcon()` null-reference failures reported by
      `ExecuteToExecuteWhenFinished()`. DDS and the deferred frame pump were not
      responsible. A Harmony patch on the static generic
      `DefDatabase<ThingCategoryDef>.ResolveAllReferences()` method corrupted Def
      resolution under Mono; a non-generic `ResetStaticDataPre()` boundary fixes it.
- [ ] Reproduce the reported colony-to-menu crash with DDS enabled and disabled.
      The captured stack points to MapModeFramework background cache work after
      teardown, so do not attribute it to DDS without an A/B result.
- [ ] Throttle or pause background conversion from CPU, I/O, RAM, and TPS budgets.
- [x] Verify DDS background completion after pack and index publication. The
      90-mod cold-cache run created 10,468 entries with 0 failures and emitted
      its typed completion report.
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

## Targeted RimWorld loading optimizations

- [x] End the direct-loader replacement experiment. The 89-mod fixture showed no
      material warm-start gain. The 260-mod fixture recovered from 259 expected
      mods to 5 and produced 4,893 relevant errors. Keep the prototype only on
      `experiments/direct-loader`.
- [x] Profile `GlobalTextureAtlasManager.BakeStaticAtlases()` by internal phase.
      Two warm-DDS runs took 653 ms and 674 ms, dominated by GPU color blitting.
      Do not replace it without new evidence on a representative slow system.
- [x] Measure repeated `ModContentPack.GetAllFilesForMod()` texture calls. The
      90-mod fixture made 180 equivalent calls; 90 repeated scans cost 68.2 ms.
- [x] Remove the duplicate FixWorld scan. RimWorld still performs the canonical
      discovery at its original deferred boundary, and DDS consumes that exact
      dictionary in a postfix. A warm 90-mod run retained 10,468 DDS hits with
      0 relevant errors and reduced `IndexTextureSources` to 0 ms.
- [ ] Profile the next largest individual RimWorld method after file discovery.
      Patch it only when the measured saving exceeds the compatibility surface.

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
