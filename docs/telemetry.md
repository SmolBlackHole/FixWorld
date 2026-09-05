# Typed telemetry and profiling

Parent: [Runtime modules](runtime-modules.md)

The active fork uses `Source/Profiling` and `Source/Telemetry`.
No references to archived source, legacy RuntimeHost, EventBus or worker scheduler
are needed. `FixWorldController.Diagnostics` owns the library's measurement
facade, profiler and store. `ModBase.Diagnostics` exposes that same instance.

## Two distinct contracts

- `Profiler<ProfileKey>` measures execution. A key has owner, operation and source
  (FixWorld or RimWorld). Resolve `ProfileSlot<ProfileKey>` once during setup.
  Use `Measure()`/`Complete()`/`Fail()` or feed raw stopwatch ticks. A failing
  operation must explicitly mark failure; scope disposal does not detect it.
- `TelemetryContract<T>` defines a stable ID, schema version, immutable DTO and
  one typed presentation function. `TelemetryStore.Register(contract)` returns
  the publication handle. No timing, gameplay aggregation or provider callbacks
  occur inside the store.

Actual request/hit/fault counts belong to the provider and continue when
profiling is disabled. Measured call counts are not authoritative business
counts. No per-sample strings, registry lookup, formatting or event dispatch.
The library uses inline profiling, with no new background aggregation thread.
Buffered profiling remains available for explicitly owned users of the profiler.

```csharp
// Initialization, not inside an operation:
var slot = Diagnostics.Profiler.GetSlot(
    new ProfileKey("dds", "lookup", ProfileSource.FixWorld));
var registration = Diagnostics.Store.Register(MySnapshot.Contract);

// Work boundary, the same API also works in a Harmony adapter:
using (var scope = slot.Measure())
{
    try { DoWork(); }
    catch { scope.Fail(); throw; }
}

// At the provider's publication boundary:
registration.Publish(new MySnapshot(/* detached values */));
// During owner cleanup, after producers stop:
registration.Dispose();
```

## Publication and lifetime

Published DTOs must be detached from mutable live state. Readers share the same
reference without copying or taking a lock. The store publishes a read-only
membership list only when membership changes. Its handles expose independently
updated values, not a globally atomic snapshot of all modules.

Duplicate IDs are rejected. Disposing a registration retires it permanently;
its last snapshot remains readable. An old handle cannot publish again or remove
a newer registration with the same ID. Store disposal retires all registrations.
Provider lifecycle stays with its existing owner; there is no new ModBase
initialization/uninstallation framework in this slice.

Library snapshots publish from the existing main-thread Update boundary at most
every 500 ms. The state capture delegate is resolved once and is only invoked
when publication is due. No snapshots are built by each probe or renderer.
Controller shutdown stops regular callbacks and disposes diagnostics after mod
quit callbacks, even when settings save fails. Hard process termination is not
a guaranteed cleanup boundary.

## Existing callers

- Controller `Update`, `Tick`, `FixedUpdate` and `OnGUI` dispatch groups use cached
  slots. Timings are inclusive of the callbacks they invoke, not total RimWorld
  tick/frame cost. Nested inclusive times must not be summed.
- Frame/tick notification counts and caught child-callback errors are independent
  business counters. Tick notifications are not a TPS measurement.
- `DistributedTickScheduler.CaptureTelemetry()` replaces the old separate
  debug count getters. It returns recipients, interval count and last-tick calls.
  Dispatch and scheduling behavior are unchanged.
- The built-in ticker test overlay reads the published `LibraryState`, not live
  counters on each GUI event.
- Existing log preparation includes published telemetry. It does not force a
  fresh capture or automatically upload anything. `Store.WriteLog(TextWriter)`
  and `Store.WriteJson(TextWriter)` use the same presentation contract. No second
  field list in the host/UI. Values carry units in their field names.

## Verification

Run `dotnet run --project mod/FixWorld/Tests/Telemetry.Contracts/FixWorld.Telemetry.Contracts.csproj -c Release`.
It compiles actual profiler/store/presentation and scheduler source for net472;
only the scheduler's engine boundary is stubbed. It checks retirement, duplicate
IDs, immutable views, concurrent publication/aggregation, disabled profiling,
cadence, JSON round trips and scheduler behavior.

Full fork compilation uses `FixWorld.csproj` with explicit `RimWorldManagedPath`
and `HarmonyAssemblyPath`. On a machine without the developer pack, set
`FrameworkPathOverride` to the net472 reference package restored by the contract
project. Override `OutputPath` and `DocumentationFile` into `temp/fork-validation`
to avoid deployment; set `GenerateSerializationAssemblies=Off` for CLI validation.
This build does not prove game behavior or Unity Mono performance.
