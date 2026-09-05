# Runtime module architecture

Parent: [Architecture](architecture.md)

Status: **binding, approved 2026-09-05**. This document records the agreed
architecture, not a claim that every existing subsystem already conforms.
Changes to its ownership boundaries or migration order require an explicit user
decision. Concrete progress belongs in [TODO](../TODO.md).

## One coherent module, shared infrastructure

```text
Hooks: adapt RimWorld or a test driver
  -> optimization: operation contract, implementation, owned state
  -> typed measurement/data contract
  -> runtime-owned telemetry store
  -> published view -> UI / log / JSON
```

These are responsibilities, not mandatory separate files. Keep a small module
together. Do not create a framework layer for each arrow.

## Ownership

- The runtime composition root owns one `RuntimeServices` instance per runtime.
  It supplies the central telemetry store, EventBus, JobScheduler and
  MainThreadQueue. Modules borrow these services; they never dispose them.
- Shared owns engine-independent contracts and reusable infrastructure. Use its
  existing profiler, scheduling and caching facilities before adding another
  implementation. No second sample bus, scheduler or aggregation thread is
  introduced by this architecture.
- An optimization owns its operation contract, implementation, state, measurement
  definitions and typed diagnostic data. It knows its own data contract. Tests
  exercise the real implementation through the same operation contract.
- Harmony/Verse/Unity adapters belong under the hook/integration layer, not in
  engine-independent core logic or Shared. During migration the existing
  `Integration` directory is that layer; a directory rename is not a prerequisite.
- The central store knows identities, schema versions, snapshot types and
  published values. It does not know how DDS or pathfinding work. Host and
  Context must not acquire new per-metric forwarding methods.

## What every module provides

Use a common typed `RuntimeModule<TSnapshot>` base for lifecycle repetition,
including modules with no active gameplay replacement. It provides:

1. A stable unique identity, positive data-schema version, and snapshot type.
2. Installation and uninstallation of its telemetry registration.
3. An optional engine-independent `IRuntimeModuleHooks` binding. Hook adapters
   implement installation and removal, including cleanup of partial installs.
4. Implementation-specific initialization and cleanup hooks, plus typed snapshot
   capture. The base does not dictate one universal gameplay operation signature.
5. Publication through the registration handle, not a string lookup per probe.

An operational module additionally defines its input/result contract and its
thread affinity, cache invalidation, state lifetime and fallback behavior.
Diagnostic data must state units, source and inclusive/nested timing semantics.
Presentation metadata belongs with that data contract. The shared presentation
interface will be introduced with the first complete module migration, not as
an unused untyped dictionary in the bootstrap slice.

## Installation and lifetime

The runtime creates shared services, creates modules and installs them before
enabling their functional hooks. Module installation proceeds in this order:

```text
register telemetry -> initialize implementation -> publish initial data
  -> install hooks -> ready
```

An installation failure unwinds acquired resources. Uninstallation removes hooks
first, cleans up module state/profilers, then removes telemetry registration.
All stages are attempted even if one cleanup stage throws. Repeated removal is
harmless. A removed/failed module instance is terminal; replacement uses a fresh
instance after the old one has been detached.

The runtime owns orchestration and serializes module lifecycle calls. Adapters
must stop new calls and resolve in-flight work before releasing module state;
an interface alone cannot make arbitrary game callbacks safe to unload.
Worker results must not target a retired instance. The runtime stops producers
and modules before releasing shared services. Constructor failure must release
already-created services too. Failure of optional measurement must not silently
change gameplay; hook owners decide and report their existing failure policy.

## Measurement and publication

### Profiler and telemetry are different responsibilities

This distinction is binding:

| Owner | Responsibility | Not its responsibility |
| --- | --- | --- |
| Shared Profiler | Time measured operations and aggregate measured calls, failures and durations | Gameplay counters, DDS state, UI or module lifecycle |
| Module | Own business counters/state, use profiler handles, combine both in its typed DTO | Reimplement Shared aggregation or route each value through Host/Context |
| Shared TelemetryStore | Register identities/schemas and expose published module data | Time operations, execute optimizations or calculate module-specific statistics |
| EventBus | Deliver discrete lifecycle/domain notifications | Transport every profiling sample or act as the telemetry store |
| Output adapters | Present published values as UI, log or JSON | Recompute domain state or start a fresh measurement per reader |

Telemetry describes the system: actual requests, cache hits, queue depth,
progress, faults, configuration and active implementation. Profiling describes
execution cost and is one optional source of telemetry. A sampled profiler call
count is not an authoritative count of actual operations. Business counters
continue when profiling is disabled. TPS is a throughput measurement, not the
inverse of a sampled method duration.

Only the module joins these sources into a diagnostic DTO. For example, DDS owns
cache-hit counts and build status, while its profiler supplies conversion and
lookup durations. Both Original and FixWorld measurements have explicit source
labels and comparable operation boundaries. Never add nested inclusive times as
if they were independent costs. The central store must stay independent of this
domain knowledge. `RuntimeTelemetryStore` currently violates this boundary; it
is migration input, not the template for the new Shared store.

### Publication contract

- Original RimWorld/Harmony operations and FixWorld implementations use the same
  measurement definitions, labelled with distinct sources. Execute only the
  selected implementation, never both implicitly when there are side effects.
- Resolve Shared `ProfileSlot<TKey>` handles once. Recording performs no
  formatting, reflection, registry lookup or event dispatch per observation.
- Modules aggregate measurements using Shared. Disabling measurement does not
  uninstall functional optimization hooks. Functional and profiling hook sets
  remain separable even if one adapter coordinates their lifetime.
- The provider publishes a detached snapshot at a bounded cadence on the thread
  allowed to read its state. The store does not call providers from a new worker.
  No per-frame diagnostic-array construction. Explicit final capture can flush
  buffered profiler data after producers have stopped.
- A registration is a typed publication handle. Reading its latest value returns
  the published reference without copying or locking. Published data must never
  be mutated or reference mutable live game state. Copy buffers only where that
  lifetime guarantee requires it. Store membership views are immutable lists;
  their handles expose live latest values, not a cross-module atomic snapshot.
- Duplicate active IDs are errors. Unregistering releases that ID. A retired
  handle cannot publish or remove a replacement registration. Retained snapshots
  remain readable. Store disposal rejects registration and further publication.
- UI, log and JSON consume the same published module data. Formatting happens at
  the output boundary. Preserve historical keys/units or explicitly version and
  migrate their consumers. Startup benchmark JSON is not silently redefined.

## Migration order and acceptance

### Phase 1: Shared foundations only

Implemented in Shared and verified by contract tests and Release build. The
runtime has not been connected to these classes. Completing this phase does not
authorize starting the runtime migration without agreeing on that next slice.

Implement the Shared service-owner class, typed store registrations and the
module base, exercised only by engine-independent tests. Do not connect them to
the running game yet. This scope was clarified explicitly on 2026-09-05: finish
the base classes before starting any runtime or diagnostics migration.

Acceptance: engine-independent contracts cover registration, duplicate IDs,
unregistration/replacement, snapshot lifetime, disposal and module installation
rollback. Shared service ownership and shutdown are tested. Repository checks and
Release build pass. Existing profiler harness remains runnable. Runtime source,
hook activation, diagnostic DTOs and log output remain unchanged.

Exclusions: no RuntimeContext wiring, no RuntimeDiagnosticsModule, no rename or
replacement of RuntimeTelemetryStore, no diagnostic DTO conversion, no removal
of existing counters, no renderer migration and no game behavior changes.
The retained 8/16/32 locality counters are measurements, not the removed grid;
review their usefulness during the diagnostics migration, not this phase.

### Phase 2: runtime, DDS and installation refactor (authorized next)

Latest decision (2026-09-05): start with DDS and the installation/startup chain,
not Pathfinding or the generic diagnostics formatter. Temporary broken builds or
game behavior during local restructuring are acceptable, but must be reported;
this is not permission to lose user data, delete caches or launch/restart games
without task-scoped authorization. Verify each completed slice before calling
it ready. Do not disguise known breakage with a parallel fallback architecture.

First cut: runtime owns Shared services; a typed DDS V2 module owns DDS startup,
attachment and lifecycle operations, its own state DTO and optional profiling.
Keep Harmony/Verse adaptation outside it. Retain the existing pack/conversion
engine as a lower-level dependency until the following DDS slices replace its
mixed responsibilities. Do not duplicate conversion or cache publication.

Then separate first-install/restart policy from filesystem installation and
game/process adapters. The first launch installs UnityDoorstop and requests one
coordinated restart. Subsequent launches enter via the preloader only when the
mod is active. Runtime readiness and later Mod attachment are distinct states.
The preloader must not create a second full runtime or scheduler. First-install
code must not require an already-running runtime to install that runtime.

V2 modules may exist beside legacy source while being built. Only one execution
path is active after a cutover. Remove the replaced ownership, branches and
forwarding in the same cutover; do not leave a permanent V1/V2 selector.

Acceptance per slice: typed contracts exercise the actual implementation, failed
startup/repeated shutdown/late attachment are covered where applicable, profiler
disable leaves business state functional, and the only active call sites use the
new owner. Preserve safety of restart coordination and worker/cache ownership.
Report build/tests separately from in-game verification. Diagnostic output
changes require explicit schema/consumer updates, not silent reinterpretation.

### Phase 3: one complete optimization module

Move pathfinding data and recording into its owning module. Bind its existing
probes and implementation to the common measurement contract. Remove the replaced
Host/Context forwarding and central pathfinding aggregation. Introduce the small
typed presentation contract with actual UI/log/JSON consumers. Test the real
operation contract, lifecycle and unchanged output meanings.

### Phase 4: apply the proven structure

Migrate remaining providers one at a time, including DDS and startup diagnostics.
Delete replaced central formatting/wiring in each slice. Do not keep a parallel
legacy system or demand repeated manual A/B game restarts for structural cleanup.

### Later: implementation exchange

Hot-reload of implementation code is a medium-term requirement. Registration,
hook removal and explicit state lifetime prepare for it but do not implement it.
Investigate the installed runtime's actual assembly/patch replacement mechanics,
in-flight calls and state transfer separately before promising code reload.

## Audit evidence behind the decision

The existing `RuntimeTelemetryStore` mixes loading, TPS, profiler slots and
pathfinding target-history/counter state. `RuntimeHost` and `RuntimeContext`
forward individual observations. `RuntimeDiagnosticsSummary` separately formats
pathfinding fields for UI and log. These are the repetitions to remove.

Shared is already used: DDS borrows the scheduler/main-thread queue, lifecycle
uses EventBus, and diagnostics uses cached profiler slots. Preserve those working
components. PerformanceOptimizer's optimization lifecycle is a reference, but its
combined Harmony, settings and measurement base is not copied wholesale; see the
[source review](research/performance-optimizer.md).
