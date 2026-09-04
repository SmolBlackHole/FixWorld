# RimThreaded architecture notes

## Scope and source snapshot

RimThreaded is a RimWorld 1.4 mod intended to execute selected simulation work on
multiple .NET threads. It is not a replacement mod loader. It is a runtime patch
layer that changes tick execution, replaces or wraps selected game methods, and
adds mod-specific compatibility patches.

This document describes the local source snapshot, not a current upstream build:

- Source snapshot metadata: `decompiled/third-party/rimthreaded/SOURCE.md`
- Upstream commit recorded by the snapshot: `c4e491805bd247ec8e38522157f876ad4ef73417`
- Snapshot date: 2022-11-01
- Project version and target: `decompiled/third-party/rimthreaded/Source/RimThreaded.csproj`
  (`2.7.2`, `net472`, RimWorld `1.4` output layout)
- The source is explicitly not compiled into or copied into FixWorld.

The README describes the intended operating model: users choose a worker count,
place RimThreaded last in the load order, and consult a compatibility list. See
`decompiled/third-party/rimthreaded/README.md`.

## Architecture at a glance

The runtime has four cooperating pieces:

1. `RimThreaded.RimThreaded` owns worker threads, per-tick coordination,
   thread-local state initialization, and the main-thread request loop.
2. `RimThreadedHarmony` installs the patches, creates thread-static replacements,
   and optionally caches the methods that need IL rewriting.
3. `RW_Patches/*` contains the replacement, synchronization, and main-thread
   bridge implementations for RimWorld and Unity APIs.
4. `Mod_Patches/*` contains hard-coded compatibility patches for selected
   third-party mods.

The static constructor in `Source/RimThreaded.cs`, `RimThreaded.RimThreaded()`,
creates `RimThreadedHarmony`, initializes the current thread's scratch state,
creates the configured workers, and starts a background monitor thread. Workers
are regular `System.Threading.Thread` instances marked `IsBackground = true`.
The default settings use eight workers and an 8,000 ms timeout, although both
values are configurable in `Source/RimThreadedSettings.cs`.

The design is therefore a coordinated simulation runner, not a free-running
background job system. A game tick is still a synchronization barrier and the
main thread still participates in the work.

## Initialization and patch strategy

`Source/RimThreadedHarmony.cs` runs its static constructor in this order:

1. Load `replacements_1.4.json`.
2. Register additional field and method replacements.
3. Apply generated field-replacement transpilers.
4. Register destructive replacements.
5. Register non-destructive patches.
6. Register explicit third-party compatibility patches.
7. Report potential Harmony conflicts.

The terms used by the source are important:

- A **destructive** patch installs a prefix that normally returns `false`, so the
  original method is skipped and the replacement owns the operation.
- A **non-destructive** patch leaves the original path in place and adds a
  prefix, postfix, or transpiler. Non-destructive prefixes are recorded in
  `RimThreadedHarmony.nonDestructivePrefixes` for conflict reporting.

The helper methods `RimThreadedHarmony.Prefix`, `Postfix`, `Transpile`, and
`TranspileMethodLock` centralize the reflection lookup and Harmony registration.
`Prefix` can infer a replacement signature from the original method, attach a
finalizer for lock cleanup, and record non-destructive prefixes. `Transpile`
catches exceptions and logs the original method and transpiler instead of
aborting the entire registration pass (`Source/RimThreadedHarmony.cs`, symbols
`Prefix`, `Postfix`, `Transpile`).

### Generated thread-static fields

`LoadFieldReplacements` reads the JSON replacement description and scans loaded
assemblies. For each configured field it emits a dynamic assembly named
`RimThreadedReplacements`, creates replacement fields marked with
`ThreadStaticAttribute`, and adds a postfix to
`RimThreaded.InitializeAllThreadStatics`. This lets a worker access isolated
scratch collections or replacement storage without sharing the original static
field (`Source/RimThreadedHarmony.cs`, `LoadFieldReplacements`).

The checked-in `Source/replacements_1.4.json` contains 86 class entries and 248
thread-static entries. These values are specific to the represented RimWorld
build and should not be treated as a general reflection convention.

### IL replacement cache

`ApplyFieldReplacements` only considers a fixed set of assemblies in this
snapshot: `Assembly-CSharp.dll`, `VFECore.dll`, `GiddyUpCore.dll`, and
`SpeakUp.dll`. It looks for methods whose IL refers to a field or method in the
replacement map, then transpiles those methods.

`Source/AssemblyCache.cs` stores the selected method name, declaring type, and
parameter types in JSON under a file named after the assembly `ModuleVersionId`.
On a cache hit it resolves the methods with Harmony `AccessTools` and skips the
full IL scan. This caches discovery, not the resulting patched methods or game
objects. The source writes the JSON with `File.WriteAllText`, so the cache has no
visible atomic-replace or corruption-recovery protocol.

## Threading model and synchronization

### Tick barrier

`RW_Patches/TickManager_Patch.cs` replaces `TickManager.DoSingleTick`. The
replacement updates the game tick, updates the shader game-seconds value, enters
`RimThreaded.MainThreadWaitLoop`, and then returns `false` so the original method
does not run.

`RimThreaded.MonitorThreads` is a separate background loop. For each tick it:

1. resets per-list counters and completion flags;
2. signals every worker's `eventWaitStart`;
3. waits for every worker's `eventWaitDone`;
4. aborts and recreates a worker that exceeds the configured timeout; and
5. signals `mainThreadWaitHandle` when all workers have finished.

Each worker waits for its start event, calls `PrepareWorkLists`, executes the
available prepared list items, runs post-work callbacks, and signals its done
event (`Source/RimThreaded.cs`, `ProcessTicks`, `PrepareWorkLists`,
`ExecuteTicks`, `MonitorThreads`). Work distribution is mostly an atomic index
pattern: the preparation phase publishes a list, and the tick phase repeatedly
decrements a shared counter with `Interlocked.Decrement` until it becomes
negative. `RW_Patches/TickList_Patch.cs` applies this pattern to normal, rare,
and long `Thing` ticks.

The worker lists are deliberately explicit. The initial list in
`RimThreaded.threadedTickLists` includes wind, normal/rare/long things, world
pawns, world objects, factions, world components, map post-ticks, transport
ships, and ideologies. `AddNormalTicking` can insert another prepared/tick pair
at runtime, which is an extension point but also mutates a shared list.

Some post-work callbacks are executed by the worker that wins an
`Interlocked.Increment` race, so they run once per tick rather than once per
worker. Examples include `DateNotifierTick`, `HistoryTick`, scenario/story
updates, game end checks, tale and quest manager ticks, world post-tick,
`GameComponentUtility.GameComponentTick`, letters, autosave, and filth monitor
work (`Source/RimThreaded.cs`, `CompletePostWorkLists`).

### Main-thread bridge

The main thread is not idle while workers run. `MainThreadWaitLoop` waits on
`mainThreadWaitHandle`, executes pending safe-function requests, drains queued
sound actions, and repeats until the monitor reports completion.

Worker-safe wrappers in Unity-facing patches use a per-worker `ThreadInfo` slot:

1. put a delegate plus boxed arguments into `safeFunctionRequest`;
2. signal `mainThreadWaitHandle`;
3. block on that worker's `eventWaitStart`;
4. read `safeFunctionResult` when the main thread signals it.

This is used by `ContentFinder_Texture2D_Patch.Get`,
`Texture2D_Patch.Internal_Create`, `ReadPixels`, and `Apply`, as well as many
audio, graphics, material, mesh, text, and Unity object operations. The wrappers
fall through to the original call when the caller is not one of RimThreaded's
workers (`Source/RW_Patches/ContentFinder_Texture2D_Patch.cs`,
`Texture2D_Patch.cs`, `AudioSource_Patch.cs`, `UnityEngine_Object_Patch.cs`).

This boundary preserves Unity's thread affinity, but the caller waits
synchronously. It moves the API call to the main thread; it does not make that
call parallel, and a high volume of small requests can turn into a serialized
queue of worker stalls.

### Locks and thread-local state

There are two main synchronization techniques:

- Hot-path scratch state is moved to `[ThreadStatic]` fields. The JSON list and
  `RimThreaded.InitializeAllThreadStatics` initialize collections for every
  worker. Examples include pathfinding queues and grids in
  `PathFinder_Patch`, reachability worklists in `Reachability_Patch`, temporary
  region state in `RegionAndRoomUpdater_Patch`, and texture-atlas temporaries in
  `PawnTextureAtlas_Patch`.
- Shared game state is protected with explicit `lock` blocks, concurrent
  collections, or `Interlocked`. `MethodLocker` can inject a
  `ReaderWriterLockSlim` around an instance, declaring type, or specific method.
  Its comments explicitly warn that recursive reader/writer combinations can
  deadlock and recommend writer locks when a read operation can call a writer
  (`Source/MethodLocker.cs`, `LockMethodOnInstance`, `LockMethodOnDeclaringType`,
  `LockMethodOn`).

The implementation still has a large lock surface. In this snapshot there are
233 `lock` occurrences in `Source/RW_Patches`, and several patches keep global
dictionaries or per-instance locks. For example, `GraphicDatabase_Patch.GetInner`
uses a double-check followed by a lock around graphic construction and
publication, while `ListerThings_Patch` locks the lister and zoom-grid maps.

## Major patched subsystems

The registration lists in `RimThreadedHarmony.PatchNonDestructiveFixes`,
`PatchDestructiveFixes`, and `PatchModCompatibility` are the authoritative
inventory for this snapshot. They cover substantially more than simulation
ticks:

- **Simulation scheduling:** `TickManager_Patch`, `TickList_Patch`,
  `WorldPawns_Patch`, `WorldObjectsHolder_Patch`, `FactionManager_Patch`,
  `WorldComponentUtility_Patch`, `Map_Patch`, `TradeShip_Patch`,
  `WindManager_Patch`, `WildPlantSpawner_Patch`, and `IdeoManager_Patch`.
- **Pathfinding and map topology:** `PathFinder_Patch`, `Reachability_Patch`,
  `Region_Patch`, `RegionMaker_Patch`, `RegionDirtyer_Patch`,
  `RegionAndRoomUpdater_Patch`, `RegionTraverser_Patch`, `RegionGrid_Patch`,
  `RegionLink_Patch`, `FloodFiller_Patch`, and `Room_Patch`.
- **Mutable registries and reservations:** `ListerThings_Patch`,
  `AttackTargetReservationManager_Patch`, `ReservationManager_Patch`,
  `PhysicalInteractionReservationManager_Patch`, `JobQueue_Patch`,
  `ThingOwnerUtility_Patch`, `MapPawns_Patch`, and lord-management patches.
- **Pawn, health, work, and AI state:** `Pawn_HealthTracker_Patch`,
  `Pawn_JobTracker_Patch`, `Pawn_PathFollower_Patch`, `Pawn_RotationTracker_Patch`,
  `PawnCapacitiesHandler_Patch`, `MemoryThoughtHandler_Patch`,
  `SituationalThoughtHandler_Patch`, `JobGiver_Work_Patch`, and several
  workgiver/verb/plant patches.
- **Graphics and audio:** `ContentFinder_Texture2D_Patch`,
  `Texture2D_Patch`, `Texture_Patch`, `GraphicDatabase_Patch`,
  `PawnTextureAtlas_Patch`, `Graphics_Patch`, `Material_Patch`, mesh and
  render-texture patches, `AudioSource_Patch`, `AudioSourceMaker_Patch`,
  `SoundStarter_Patch`, `SustainerManager_Patch`, and sound-size patches.
- **Lifecycle and deferred work:** `LongEventHandler_Patch` changes the
  completion queue and thread initialization behavior. `Map_Transpile` starts a
  dedicated background thread for `SkyManager.SkyManagerUpdate` and coordinates
  it with an `AutoResetEvent`.
- **Compatibility:** `PatchModCompatibility` explicitly calls compatibility
  modules for Android Tiers, Awesome Inventory, Children, Combat Extended,
  Dubs Skylight, GiddyUp, Hospitality, Jobs of Opportunity, Map Reroll, Pawn
  Rules, ZombieLand, VEE, SOS2, SpeakUp, RimWar, TD Enhancement, Fluffy
  Breakdowns, Better Message Placement, Turn It On and Off, Alien Race, and
  Dubs Bad Hygiene.

The source inventory contains 206 files under `Source/RW_Patches`. This breadth
explains the compatibility burden: correctness depends not only on the worker
runner but also on every replacement preserving game invariants and interacting
correctly with other Harmony patches.

## Deferred work, Unity affinity, and failure behavior

`LongEventHandler_Patch` replaces the completion execution path with a
`ConcurrentQueue<Action>`. `ExecuteWhenFinished` enqueues actions and returns;
`ExecuteToExecuteWhenFinished` drains the queue on the executing thread, profiles
each action, catches and logs exceptions, and finally clears the original
`LongEventHandler.toExecuteWhenFinished` list. This is a queue substitution, not
parallel execution of arbitrary deferred delegates (`Source/RW_Patches/
LongEventHandler_Patch.cs`, `ExecuteWhenFinished`, `ExecuteToExecuteWhenFinished`).

The worker bridge catches Unity's thread-affine operations by returning to the
main thread. Audio and graphics wrappers use the same event-based request path.
The implementation has no general cancellation or shutdown protocol for those
requests. A worker waiting for a main-thread response relies on the main loop
continuing to service `MainThreadWaitLoop`.

Error handling is local and intentionally permissive in several places:

- Individual `Thing` ticks and many post-tick callbacks catch exceptions and log
  them so one failure does not stop the entire tick (`RimThreaded.cs`,
  `CompletePostWorkLists`; `TickList_Patch.cs`).
- Transpiler failures are caught and logged by `RimThreadedHarmony.Transpile`.
- A worker that exceeds `timeoutMS` is logged, aborted with `Thread.Abort`,
  removed, and replaced (`RimThreaded.AbortThread`).
- Deferred actions catch exceptions individually.

These choices provide availability at the cost of diagnosability and state
integrity. An aborted worker may have been part-way through mutating shared game
state. A logged Harmony error may leave only part of the intended patch set
active. The source comments themselves mark many areas as unresolved, for
example excessive locks, unknown root causes, and possible replacement with
thread-safe collections (`RimThreadedHarmony.PatchDestructiveFixes`).

## Compatibility and safety trade-offs

The approach makes a clear trade: broad speed gains are purchased with a broad
semantic rewrite of a mutable, historically single-threaded game.

Positive properties:

- Explicit work lists expose real parallel units instead of starting arbitrary
  tasks around unknown game code.
- Thread-static scratch data avoids sharing common algorithmic buffers.
- Main-thread bridges protect Unity calls that cannot safely execute on workers.
- The module-version cache avoids repeating expensive IL discovery after an
  unchanged assembly is seen.
- Mod-specific patches acknowledge that compatibility is a first-class concern.

Risks and limitations:

- The patch set is strongly version-specific: the JSON is named for RimWorld
  1.4, and the project references a particular game assembly layout.
- Destructive prefixes replace behavior rather than composing with it. A missed
  invariant or a conflicting Harmony patch can change game semantics.
- Synchronous main-thread requests can serialize workers and can deadlock if the
  main-thread service loop is unavailable.
- Locks around broad game objects can turn contention into stalls and introduce
  lock-order deadlocks. `MethodLocker` explicitly documents this risk.
- `Thread.Abort` is a forceful recovery mechanism, not transactional rollback.
- Persistent replacement caches are not visibly atomic and are keyed only by a
  module version ID, not by a full compatibility manifest.
- Unity-facing methods remain main-thread work. The architecture therefore does
  not provide a general solution for parallel texture upload, graphics creation,
  audio operations, or other engine-owned side effects.

## Reusable lessons for FixWorld

The useful ideas are narrower than the whole RimThreaded patch set:

1. **Separate preparation from publication.** File discovery, byte reads,
   decompression, parsing, and cache validation are good candidates for worker
   execution. Unity object creation and publication should cross one explicit,
   observable main-thread boundary.
2. **Use per-worker scratch state only where the algorithm needs it.** A
   thread-local parser buffer or pathfinding workspace is safer than cloning
   whole mutable game objects. Initialize it once per worker and keep ownership
   explicit.
3. **Batch main-thread work.** RimThreaded's per-call request bridge is a useful
   correctness boundary but a poor high-volume transport. FixWorld should submit
   batches of uploads or lifecycle actions and measure queue wait time separately
   from execution time.
4. **Keep cache scope and invalidation explicit.** The IL cache demonstrates the
   value of caching discovery, but its module-version key and non-atomic write
   path are not sufficient as a general cache contract. FixWorld manifests should
   include the relevant game/runtime/schema inputs and use atomic replacement.
5. **Prefer measured, narrow patches.** The explicit registration lists and
   comments show how quickly a performance project becomes a compatibility layer.
   Patch one measured method or boundary at a time, with an immediate fallback to
   the original path.
6. **Treat deferred work as a semantic boundary.** Replacing a completion queue
   can preserve ordering while changing thread ownership. Record who schedules,
   who executes, and which operations are allowed on each side before changing
   it.
7. **Instrument synchronization, not just elapsed stages.** Worker idle time,
   main-thread bridge wait time, lock contention, queue depth, and timeout/retry
   counts are the measurements needed to tell real parallel work from serialized
   handoffs.
8. **Do not infer that a broad runtime rewrite is required for asset wins.**
   RimThreaded's texture and graphics patches still marshal engine calls to the
   main thread. FixWorld can retain RimWorld's loader and independently optimize
   indexed file discovery, DDS preparation, cache reuse, and controlled Unity
   publication.

## Bottom line

RimThreaded demonstrates a technically coherent but very invasive strategy:
replace the tick barrier, split selected list processing across workers, isolate
scratch state, lock shared structures, and marshal Unity calls synchronously to
the main thread. Its strongest reusable concepts are ownership separation,
thread-local preparation state, explicit barriers, and versioned discovery
caches. Its broad patch inventory, forceful timeout recovery, and per-call
main-thread bridge are precisely the parts FixWorld should treat as compatibility
and operational warnings, not as a template for the loading pipeline.
