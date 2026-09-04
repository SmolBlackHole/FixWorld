# Runtime diagnostics

Parent: [Documentation index](README.md)

The Runtime owns one telemetry store for startup diagnostics. The Loader and
normal Mod expose its output at their boundaries; they do not maintain parallel
profiling systems.

## Data flow

The Runtime telemetry store records passive RimWorld stage boundaries directly.
It uses one pre-registered shared profiler slot per stage and creates one
immutable schema 19 snapshot containing:

- the preloader timeline and DDS read-ahead result;
- all 17 loading-stage timings, call counts, and failure counts;
- DDS cache counters;
- configured scheduler workers, pending main-thread actions, and system memory.

The snapshot is the source for all three outputs:

1. a compact RimWorld log summary;
2. versioned JSON consumed by `tools/benchmark.py` when benchmarking is enabled;
3. formatted read-only text exposed through the Runtime contract to the Mod UI.

Python validates the JSON schema, writes `loader-stages.csv`, and appends a
selected aggregate result row. It does not reconstruct timings from the RimWorld
log.

## Shared profiler

The shared profiler separates registration, recording, aggregation, and reading:

1. Register a named slot once and retain the returned `ProfileSlot<TKey>`.
2. Record raw `Stopwatch` ticks through that slot. The hotpath performs no key
   lookup, string formatting, event dispatch, or `TimeSpan` conversion.
3. Publish one immutable snapshot at the configured cadence.
4. Let logs, diagnostics, and UI reuse the same published snapshot reference.

`ProfileScope<TKey>` is a stack-only value type. Creating and disposing it does
not allocate. Measurements retain raw `Stopwatch` ticks and convert them only
when a reader asks for `TimeSpan` values.

Two aggregation modes serve different workloads:

- `Inline` updates atomic counters immediately. It is the lower-overhead choice
  for one producer or rare lifecycle boundaries.
- `Buffered` gives each producer thread a double-buffered local aggregate. One
  background thread swaps and merges those aggregates, then publishes a snapshot
  every 500 ms by default. It avoids cross-core counter contention and does not
  queue or allocate an object per observation.

Startup stage telemetry uses `Inline` because it records only 17 ordered
boundaries from one producer. Concurrent tick profilers can select `Buffered`.

The buffered mode publishes only after new observations or an explicit flush.
It therefore creates no periodic snapshots while idle. Disabling a profiler
turns a timed scope into a cheap branch and does not start formatting or event
work.

Live inline snapshots are eventually consistent while another thread is writing.
An explicit snapshot after producers stop is exact. Buffered snapshots are
merged and published by their sole aggregation thread. `Snapshot()` is a cold
control operation that flushes pending producer aggregates; hot readers should
use `PublishedSnapshot` instead.

The general event bus is deliberately absent from the measurement path. It
remains appropriate for low-frequency lifecycle notifications, not profiler
samples.

Run the optimized microbenchmark with:

```powershell
dotnet run --project mod/FixWorld/Tests/Profiling.Benchmarks -c Release
```

The harness compares disabled probes, inline aggregation, buffered aggregation,
timed scopes, and eight concurrent producers. It also reports Gen 0 collections
and verifies the accepted observation count. Desktop CLR results are a design
check, not proof of Unity Mono performance; every new in-game hotpath still needs
a representative RimWorld measurement.

## In-game UI

The normal Mod presents Startup, Preloader, Stages, DDS cache, Runtime,
and Issues sections in a resizable window. Dense stage rows scroll independently.
The Runtime formats the retained snapshot once; an open window polls the stable
text contract at most every 500 ms. A closed window does no polling or formatting
work.

The UI is observational. Opening it does not install Harmony patches, activate
additional profilers, change scheduler behavior, or mutate the completed
snapshot.

The event bus is reserved for typed runtime notifications such as lifecycle
changes. It is not a second telemetry store. Channel and subscriber snapshots
are rebuilt only when their registrations change, so an idle frame pump does not
allocate.

## Current limits

Stage timings show where startup time is spent, but they do not attribute nested
work to individual mods or methods. The scheduler snapshot reports configured
workers and queued main-thread work, not measured utilization. Expensive texture
method transpilers and per-stage process sampling are intentionally outside the
always-on telemetry path.

Open diagnostics work is tracked in the [TODO](../TODO.md). Benchmark operation
and reproducibility rules are documented in
[Development and verification](development.md).
