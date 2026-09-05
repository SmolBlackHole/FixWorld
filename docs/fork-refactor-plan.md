# Fork infrastructure refactor

Parent: [Runtime modules](runtime-modules.md)

Status: approved for autonomous execution by the user on 2026-09-05.
This is the current slice plan. It supersedes the older Foundation-wrapper and
Shared-import migration order. The active code is the HugsLib fork in
`mod/FixWorld`; `.archive/FixWorld` is reference material, never a build input.

## Working agreement

For each slice: **Explore -> Plan -> Implement -> Verify -> Commit**.
Use the corresponding skills. Finish one slice before starting another.
Routine implementation choices do not require another user approval.

Explore actual fork code, its callers and archived reusable implementations.
Record the smallest complete cutover, including ownership, exclusions and tests.
Implement the shared contract and replace the affected callers, not a parallel
compatibility layer. Verification is a separate read-only pass. A defect returns
to Implement, followed by another Verify pass. Commit only the finished slice.
Do not push, deploy, start the game, change `.editorconfig`, or touch the archive.
Preserve other work, including the user's formatting commit.

## Slice 1: typed profiling and telemetry (complete)

Evidence: `FixWorldController` already dispatches all library callbacks.
`DistributedTickScheduler` exposes individual debug counters. `ModLogger` and
`LogPublisher` format output but do not provide a measurement store. The archived
profiler supplies cached slots and bounded-frequency snapshots independently of
the engine. No second event bus or worker scheduler is needed for this slice.

1. Import only the profiler implementation needed for measurement.
2. Add typed metric identities and typed, versioned telemetry registrations.
   One presentation contract renders the same snapshot into log and JSON.
3. Controller owns the measurement facade. Core recording/store/formatting are
   engine-independent; module code can use the same APIs as controller probes.
4. Instrument library callback boundaries with cached slots. Publish business
   counters and timing at a bounded wall-clock cadence, not per probe/frame.
   Use the existing scheduler counters through one typed snapshot, replacing its
   separate count getters and callers.
5. Expose the published data through the existing log export. No automatic
   external upload or per-tick log formatting.
6. Verify typed identity, retirement/replacement, immutable retained views,
   disabled profiling with live business counters, concurrent recording,
   presentation consistency, cadence and disposal. Compile engine-independent
   contracts and all fork source against local game references where available.

Excluded: module lifecycle redesign, ModBase<T>, caches, scheduling behavior,
DDS, preloader, installer, hot reload, UI redesign and game-performance claims.
The facade is an owned measurement service, not another RuntimeHost.

## Slice 2: typed caching (complete)

Begin only after Slice 1 is verified and committed. Audit real cached values and
their invalidation and lifetime. Build the shared typed cache contract and use
the Slice 1 telemetry API for its counters. Replace the selected fork cache
callers completely, preserving bounded memory, exact cache keys and engine
thread ownership. Immutable snapshot caches are not automatically suitable for
every tiny UI memo: choose the smallest implementation fitting the contract.
Record exact consumers and acceptance before editing this slice.

### Explored cutover and acceptance

`Settings/CachedLabel` independently memoizes size and height. Its height key
truncates the width to int and omits font state; `Dialog_ModSettings` retains one
wrapper per control even if the title changes. The only current caller needs
height, not the wrapper's unused size/translation helpers.

- Add one engine-independent `CacheStore` with typed, named cache contracts and
  retained handles. Cache values stay with their owner. Use bounded FIFO eviction,
  explicit invalidation and owner-thread access, not a globally locked object bag
  or full dictionary copies on every UI miss.
- The controller owns the registry and publishes cache statistics using Slice 1.
  Factory measurements use its shared profiler. A cache handle never resolves
  names or allocates a delegate on hits. Dispose unregisters its telemetry.
  Bind cache use from main-thread Update; allow setup registrations before that.
  Async defs reload requests a generation invalidation applied on the owner.
- Replace `CachedLabel` and the per-control cached title with a central text-height
  cache. Keys include actual text, exact float width, font, UI scale and language.
  Invalidate on defs reload. Use a bounded capacity and restore font state after
  a miss. Do not change GUI layout, scheduler, loading or game behavior.
- Tests cover hit/miss, zero/null values, FIFO bound, invalidation, failed factory,
  duplicate IDs, wrong-thread calls, disposal, telemetry while profiling is off,
  exact-width/font/title changes using the real text-cache adapter with engine
  stubs. Re-run Slice 1 contracts and full fork compilation before committing.

Retained bindings such as `OptionsDialogExtensions` FieldInfo references are
resolved engine contracts, not mutable data caches; do not turn each into a
dictionary lookup. News image ownership and its dependent lists require a
separate resource-lifetime slice. Exploration found an existing defect there:
`DestroyLoadedImages` destroys borrowed ContentFinder/placeholder textures too.
This defect is documented in TODO; no game readiness claim follows this slice.

## Deferred

Scheduling/jobs require their own later slice. Do not import them now. Likewise,
do not revive old runtime orchestration or move DDS/installer code in this task.

## Verification record

- Slice 1: PASS, 35 net472 contract checks; full fork Release build against the
  local RimWorld Managed directory and Harmony 2.4.1, zero warnings/errors.
  Scope smoke: about 5 ms per 100,000 scopes, zero Gen0 collections in that
  desktop CLR interval. Not an allocation proof or Unity Mono performance claim.
  Old scheduler count getters have no remaining callers; no cache/job imports.
  In-game behavior remains unverified. See [telemetry](telemetry.md) for contracts.
- Slice 2: PASS, 36 cache contract checks plus all 35 telemetry regression
  checks. Full local-reference Release build: zero warnings/errors. Old
  CachedLabel/per-control title cache has no remaining callers. 100,000 cache
  hits took about 1.6 ms with zero Gen0 collections in the desktop CLR smoke
  interval, not a Unity Mono benchmark. Release compiler optimization enabled.
  News texture ownership remains explicitly open; no in-game test performed.

The authorized two-slice run is complete. Scheduling/jobs, DDS and installation
have not been migrated. Further work starts with a new scoped exploration.
