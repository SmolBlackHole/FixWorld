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
This defect is addressed by the subsequent News resource-lifetime slice below;
no game readiness claim follows the cache slice.

## Slice 3: News image lifetime (complete)

The user's latest decision advances this fix ahead of scheduling. Keep the
existing News UI and scheduler. Do not build a changelog publishing system here.

- Represent owned file textures and borrowed ContentFinder/placeholder textures
  explicitly. Destroy only owned resources, including partially decoded images.
- One window-owned image set uses mod identity plus filename and deduplicates
  requests. Clear it at replacement and the actual Window.PostClose boundary.
- Generation-check queued loads so a closed/replaced window cannot load stale
  images or clear the pending flag of a newer batch.
- Verify production loader and owner with deterministic engine stubs: ownership,
  duplicate names, close-before-pump, replacement, empty news, decode/processing
  failures and enqueue failure. Compile the complete fork and rerun both earlier
  contract suites. No game launch or deployment in this slice.

## Deferred

Scheduling/jobs and DDS follow the user-prioritized bootstrap slice. Do not
revive the archived Loader/Runtime chain, DDS, loading UI or scheduler here.

## Slice 4: bootstrap, installation and restart (implemented, in-game acceptance open)

Approved by the user after the archive/fork audit. First normal Mod construction
installs Doorstop and requests one coordinated restart. On subsequent enabled
launches the in-process preloader starts only the controller's engine-independent
core; normal Mod construction attaches ModContentPack/settings to that same
instance. Existing late initialization and Unity callbacks remain with the fork.

- One engine-independent FixWorld.Bootstrap assembly in the canonical v1.6
  Assemblies directory supplies explicit lifecycle states, installation and
  restart contracts. Doorstop targets its entry point. No duplicate runtime DLL.
- Wait for Assembly-CSharp and the Harmony assembly actually loaded by the game,
  then load the canonical adjacent FixWorld.dll once. Verify assembly identity
  when the normal Mod attaches. No heuristic Harmony search or hardcoded MVID.
- A completed state is published only after success. Installation-only, disabled,
  failed and restart-pending launches cannot run the late initializer. Core
  services are created once and disposed on failed attach/shutdown.
- Reuse installation invariants: versioned manifest, verified bundled proxy,
  owned-file checks, atomic per-file writes, repair and restart-loop prevention.
  Remove the old migration deleting FixWorld.dll. No live installation changes
  during implementation. An existing foreign/legacy installation is not adopted
  silently.
- One dedicated helper validates launch arguments/parent identity, acknowledges
  readiness, waits for commit and parent exit, clears inherited loader markers,
  then launches once. All GenCommandLine.Restart callers use one Harmony adapter.
- Tests cover phase ordering, duplicate/failing start, assembly identity,
  activation/config paths, install/repair/conflicts/pending confirmation and real
  child-process restart handshakes. Build every changed project and rerun the
  93 earlier contracts. Real Doorstop/Unity startup remains an explicit in-game
  acceptance step; do not claim it from desktop tests.

## Slice 5: local capture and Python harness (complete, in-game acceptance open)

Approved after the shell/export exploration. Shell only opens processes/files;
it is not an IPC endpoint. Reuse typed presenters, not the archived schema-19
loader report or a second measurement registry.

1. Mark cumulative counters in the shared presentation contract. Export all
   registered modules generically, including registration lifetime identity.
2. Controller owns a background JSONL exporter of published snapshots (one
   second cadence, unique process session, bounded file size, isolated errors).
   No gameplay reads, formatting or file I/O in probes. Stop before store teardown.
3. Python collects complete lines and local logs without starting/stopping the
   game. Preserve session boundaries and raw data; derive only declared counter
   deltas. Publish generic CSV and summaries, tolerate incomplete final lines.
4. Test production serialization, background lifetime/failure, new contracts,
   Python parsing, restarts, counter resets and live partial writes. Build the
   fork and rerun existing contracts. Native in-game acceptance remains separate.

Excluded: remote commands, uploads, DDS, new gameplay probes, scheduler redesign,
legacy benchmark compatibility and automatic game termination.

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
- Slice 3: PASS, 22 production News loader/owner contract checks plus the 71
  telemetry/cache regression checks. Full local-reference Release build:
  zero warnings/errors. Removed the old untyped image-loading API. Source
  inspection confirmed WindowStack calls PostClose on direct removal. Actual
  Unity decoding, rendering and in-game window behavior remain unverified.
- Slice 4: local verification PASS. 63 bootstrap checks, including acknowledged
  helper/parent/child process fixtures, actual managed entry enabled/disabled,
  same assembly/controller/service graph, and nonblocking state reads. All 93
  earlier contracts still pass. Complete local-reference build: zero warnings
  and errors. Bundled Doorstop hash matches the archived pinned binary. No
  native game process, live installation or deployment performed. The in-game
  first-install/attach/restart/disable sequence remains an acceptance item.

- Slice 5: local verification PASS. 35 telemetry + 13 capture/finalizer checks,
  36 cache, 22 News and 63 bootstrap checks. All 11 Python collector tests pass,
  including mid-collection session replacement and partial UTF-8/log writes.
  Pyright strict: zero errors/warnings. Full local-reference Release build:
  zero warnings/errors. Production C# fixture -> Python analyzer preserves an
  arbitrary module/schema and computes requests 7 -> 17 as delta 10 without a
  module-specific parser. No game, deployment or external upload performed.
  Concurrent user formatting/modernization retained; unavailable net472
  ThrowIfNull calls use explicit checks. Native capture acceptance remains open.

Autonomous continuation remains authorized, one scoped slice at a time.
Scheduling/jobs follow the user-prioritized capture slice. DDS remains excluded. Do not treat desktop
bootstrap verification as proof of native Doorstop/Unity Mono behavior.
