# Runtime diagnostics

Parent: [Documentation index](README.md)

The Runtime owns one telemetry store for startup and live hotpath diagnostics.
The Loader and normal Mod expose its output at their boundaries; they do not
maintain parallel profiling systems.

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

## Runtime hotpaths

The same telemetry store owns one long-lived `Buffered` profiler. Runtime hooks
measure RimWorld's existing tick and pathfinding boundaries without replacing
their behavior:

- `TickManager.DoSingleTick`, `MapPreTick`, and `MapPostTick`;
- pathfinder ticks, synchronous requests, map-data gathering, the Unity Job
  completion barrier, and grid/path scheduling;
- each incremental path-data source used by map-data gathering;
- reachability checks and reachability-cache lookups.

The path-scheduling boundary also accumulates batch size and queue delay in game
ticks. Grid-job creation and reachability-cache outcomes use atomic counters.
These values are aggregated directly and do not enqueue sample objects or pass
through the event bus. Private pathfinder hooks are an optional diagnostic
group: failure to resolve one disables that group without disabling FixWorld's
loading or DDS features.

Readers use the last immutable published snapshot and its monotonic publication
timestamp. Formatting happens only when a consumer asks for diagnostics.
While a game is active, FixWorld also writes a cumulative
`[FixWorld.Profile]` entry to `Player.log` every 30 seconds and when the game
ends. Each hotpath uses `calls,totalMs,avgMs,maxMs`; pathfinding counters follow
in the same structured line.

## In-game UI

The normal Mod presents Startup, Preloader, Stages, DDS cache, Runtime, Issues,
Hotpaths, and Pathfinding sections in a resizable window. Dense rows scroll
independently. The Runtime retains the formatted startup result and appends the
latest published runtime snapshot when requested. An open window polls the text
contract at most every 500 ms. A closed window does no polling or formatting
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
work to individual mods or methods. Runtime hotpath totals are inclusive and can
overlap. Queue delay is currently measured in game ticks, not wall-clock time.
The scheduler snapshot reports configured workers and queued main-thread work,
not measured utilization. Expensive texture method transpilers and per-stage
process sampling are intentionally outside the always-on telemetry path.

Open diagnostics work is tracked in the [TODO](../TODO.md). Benchmark operation
and reproducibility rules are documented in
[Development and verification](development.md).
