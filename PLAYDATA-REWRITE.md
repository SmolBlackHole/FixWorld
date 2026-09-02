# FixWorld PlayData Rewrite

Status: **Approved and active**

This plan replaces the current loading architecture behind
`PlayDataLoader.DoPlayLoad()`. The existing loading implementation is evidence
for RimWorld behavior, not a permanent dependency of the replacement.

## Objective

FixWorld owns the complete play-data load through one early hook, one runtime
composition root, and one instance-based pipeline. Loading UI, telemetry,
lifecycle events, deferred work, and later worker preparation consume the same
typed runtime services.

The rewrite is behavior-identical first. Performance work starts only after the
replacement passes the full compatibility suite.

## Decisions

- `RuntimeHost` is the composition root and owns one `RuntimeContext`.
- `RuntimeContext` owns the Shared EventBus, scheduler, play-data pipeline,
  lifecycle service, loading state, and telemetry.
- Dependencies are passed explicitly through constructors. No DI framework is
  introduced.
- Harmony patches are static translation boundaries only. They forward to the
  active `RuntimeContext` and contain no loading policy.
- The new pipeline does not call old FixWorld loading coordinators or adapters.
- RimWorld operations that are not yet replaced live behind small explicit
  stage adapters.
- There is one active loading path. No compatibility API or long-lived second
  FixWorld pipeline is retained.
- Failure before FixWorld claims `DoPlayLoad()` may fall back to Vanilla.
  Failure after ownership is claimed is reported and terminates that load.
- Shared primitives are referenced as a normal assembly. Runtime source files
  are not linked duplicates of Shared contracts.

## Keep

- Doorstop installation and recovery
- `FixWorld.Preloader`, `FixWorld.Loader`, version and contract validation
- `FixWorld.Shared` caching, events, profiling, and scheduling primitives
- the normal mod attachment and settings bridge
- the texconv and DDS cleanup tool boundary
- the current DDS implementation behind a single texture-cache adapter until a
  separate DDS replacement is approved
- benchmark capture and the existing pilot data
- the loading UI presentation, with its data source replaced

## Replace

- static runtime service access with `RuntimeContext`
- static `PlayDataLoadPipeline` with one constructed pipeline
- `FixWorldEvents` with the EventBus owned by `RuntimeContext`
- old loading event and telemetry producers with pipeline observers
- old lifecycle globals with a lifecycle service owned by the context
- delegate reconstruction for deferred work with typed work captured at enqueue
- runtime scheduler facades with direct use of the Shared scheduling contracts
- mixed loading hooks with small integration patches that only translate calls

## Delete at cutover

- `LoadingCoordinator`
- `LoadingStageExecutor`
- `LoadingWork`
- `VanillaLoadingActionAdapter`
- `VanillaDelayedActionBridge`
- `ContentLoadingPipeline`
- `FinalizationPipeline`
- `ModBootPipeline`
- `ModBootStageRunner`
- `FixWorldEvents`
- obsolete loading models, telemetry, events, hooks, and scheduler facades that
  have no remaining callers

Deletion is reference-driven. A file is removed in the same cutover that
removes its last supported caller.

## Target ownership

```text
Doorstop
  -> FixWorld.Preloader
  -> FixWorld.Loader
  -> RuntimeHost
     -> RuntimeContext
        -> EventBus
        -> Scheduler
        -> PlayDataPipeline
        -> LifecycleService
        -> LoadingState
        -> LoadingTelemetry

Harmony DoPlayLoad prefix
  -> RuntimeHost.Current.PlayData.Load()

PlayDataPipeline
  -> stage runner
  -> explicit RimWorld adapters
  -> typed stage events

EventBus subscribers
  -> UI state
  -> telemetry
  -> benchmark recorder
  -> lifecycle consumers
```

## Play-data stages

The initial replacement preserves RimWorld's order:

1. Reset
2. Initialize mods
3. Prepare mod content
4. Create mod classes
5. Load and patch XML
6. Import definitions
7. Early binding
8. Generate pre-resolve implied definitions
9. Resolve cross-references
10. Resolve definitions
11. Generate post-resolve implied definitions
12. Finalize definitions
13. Initialize runtime data
14. Execute deferred main-thread work
15. Complete

Stages may be split only where ownership, thread affinity, or an observed
performance boundary requires it.

## Implementation phases

### 1. Composition root

- introduce `RuntimeContext` and explicit service ownership
- reference `FixWorld.Shared` normally and remove Runtime's linked Shared copies
- move EventBus and scheduler lifetime into the context
- keep Harmony patches as the only static boundary

Acceptance:

- Shared contracts and full build pass
- runtime services are created and disposed exactly once
- no `extern alias FixWorldShared` remains in Runtime

### 2. Replacement pipeline

- create an instance-based pipeline and stage runner
- implement the complete behavior-identical sequence
- implement mod initialization, content preparation, class creation, XML, defs,
  binding, finalization, and completion without old FixWorld coordinators
- invoke remaining RimWorld internals only through explicit adapters

Acceptance:

- `DoPlayLoad()` has one FixWorld owner
- active mod list and order are identical to the baseline
- stage order is deterministic and monotonic

### 3. Runtime consumers

- connect UI state, telemetry, benchmarks, and lifecycle to typed bus events
- capture deferred work when it is enqueued
- execute deferred commits in deterministic main-thread order
- retain no direct UI or benchmark writes in pipeline code

Acceptance:

- UI continues to render during the load
- every stage reports start, completion or failure
- subscriber failure cannot stop another subscriber

### 4. Direct cutover and deletion

- point the early `DoPlayLoad()` hook at the new context
- delete the replaced loading architecture and obsolete hooks
- remove Runtime scheduling and event facades whose ownership moved to Shared
- keep no backwards-compatibility path between the two FixWorld pipelines

Acceptance:

- deleted types have no references
- build contains no duplicate orchestration path
- startup failure before ownership still reaches Vanilla safely

### 5. Compatibility verification

- Shared contracts
- full solution build with zero warnings and errors
- full 88-mod load to the definitive main-menu signal
- exact active-mod order hash parity
- monotonic loading UI stages
- benchmark report with zero relevant errors
- Quarry save load and return to menu
- second game in the same process and clean shutdown

## Deferred work

- performance optimization
- actual parallel execution
- Unity Jobs prototype
- DDS redesign
- cache packing
- Harmony routing beyond the operations required by this pipeline
- ingame TPS and pathfinding work

Any of these requires a separate measured decision after the behavior-identical
replacement is stable.

## Stop conditions

Stop and revise this plan if:

- RimWorld 1.6 cannot preserve an operation's semantics without the original
  LongEvent mechanism
- a supported mod depends on an observable ordering contract that the target
  stages do not model
- normal Shared assembly references cause an unavoidable type-identity conflict
- compatibility requires maintaining two active FixWorld loading paths
