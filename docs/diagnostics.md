# Runtime diagnostics

Parent: [Documentation index](README.md)

## Runtime

`FixWorldController` owns `LibraryDiagnostics`, its profiler and `TelemetryStore`.
Modules register typed, versioned contracts and publish immutable snapshots.
The presenter supplies local JSONL capture and log text. Structured UI pages read
the same snapshots directly, without a second DTO hierarchy or a log parser.
See [Telemetry](telemetry.md) and the [Python harness](harness.md).

The restored loading screen keeps the centered dark panel, cyan rails and native
RimWorld text. Its progress bar represents completed stage boundaries, not an
estimate of remaining time. The `Tip` row rotates the archived English tips every
eight seconds, without a blue side stripe. Memory text refreshes twice per second.

`fixworld.loading` schema 1 reports stage, elapsed time at the last transition,
durations and failure text. Publication occurs only at transitions. The overlay
computes running elapsed time from the monotonic start timestamp. Durations are
gauges, not counters across retries. Unobserved early boundaries remain zero.

During deferred content callbacks the isolated frame draws the menu background
and FixWorld panel, without invoking content-dependent vanilla tip/mod-summary
windows. Omitting that background caused the reported brief blank image.

The `FixWorld` main-bar entry toggles a resizable native diagnostics window.
`Options -> Mod options -> FixWorld` opens the same diagnostics window, including
from the main menu. Navigation provides Overview, DDS cache, Settings and Technical
details. General settings are embedded; DDS limits and reserve are on the DDS page.
Both use the same SettingsPanel as the standalone settings dialog, including
validation, hover info/context icons and reset. Valid text edits commit and save
when leaving the page or closing the window. Other mods keep their own settings.

Overview and DDS show labeled values with units. Technical details exposes all
registered telemetry, with an optional contract selector. Only this page formats
raw text periodically. Each page retains its scroll position across refreshes and
navigation. Settings controls are retained rather than recreated on refresh.
`Write to log` exports the complete current snapshot locally, regardless of page.

UiTheme shares dark/cyan colors with the loading screen. Early rendering does not
load settings icons or access the later window stack. There is no theme editor.

### Current verification

15 settings-panel contracts exercise the production renderer with stubbed Unity
drawing: typed validation, pending-edit commit, filtered reset, value refresh,
scroll retention and event unsubscription. Native layout and interaction with
the new embedded panels are not yet verified.

32 loading-model/tip contracts and 14 production deferred-pump checks pass.
Native run `temp/ui-background-20260905-214555/Player.log` reached the main menu;
screenshots confirmed the panel, Tip and background during Def and deferred work.
Its live JSONL (process 47132) reports bootstrap `Ready`, loading `Complete`,
without a loading failure. The later constructor-time cross-reference guard is
contract-tested; its native run is pending, as are main-bar interaction/scrolling.

Windows directory listings can report stale zero lengths for open captures.
Read complete lines through the collector before diagnosing lost data.
The mod pack still logs texture-size warnings and a translation-error summary.

## Archived predecessor (not active in the fork)

The following describes the archived implementation only. Its Runtime, Loader,
schema 19, pathfinding probes and DDS controls have not all returned in the fork.
The current behavior above takes precedence; historical measurements remain
useful references, not claims about deployed features.

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
The read-only snapshot DTOs use primary constructors without changing their
get-only API or equality behavior.
While a game is active, FixWorld also writes a cumulative
`[FixWorld.Profile]` entry to `Player.log` every 30 seconds and when the game
ends. Each hotpath uses `calls,totalMs,avgMs,maxMs`; pathfinding counters follow
in the same structured line.

Pathfinding demand is observed once when RimWorld creates a `PathRequest`, not
from every pawn tick. Fixed counters record pawn category, traversal mode, end
mode, target kind, Manhattan-distance bucket, mutually exclusive 8 by 8, 16 by
16, and 32 by 32 locality, and active path constraints. A
bounded four-way set-associative tracker with 4,096 slots counts the same map
target and end mode appearing again within 600 game ticks. Expired entries are
reused without counting a collision. Tracker collisions mean all four entries
in the selected set were still live, so the repetition rate remains explicitly
approximate when the table is saturated.

Connectivity invalidation is sampled from the final dirty-cell list passed to
`ConnectivitySource`. FixWorld counts the raw 3 by 3 cell visits, the unique
expanded cells, and the affected 8 by 8, 16 by 16, and 32 by 32 chunks. Each
worker reuses its scratch sets. The recording path does not allocate after a
worker's scratch storage has reached the required capacity. Snapshot arrays are
copied only when diagnostics are read or published.

`FixWorld path request telemetry` and `FixWorld path spatial telemetry` are
profiled as normal hotpaths. Their reported totals expose the observer cost in
the same runtime snapshot as the game work being studied.

The removed super-grid experiment and its final measurements are documented in
[Pathfinding optimization](pathfinding.md#rejected-81632-super-grid-experiment).
It no longer adds hotpaths, snapshots, UI sections or gameplay hooks.

## In-game UI

The normal Mod presents Startup, Preloader, Stages, DDS cache, Runtime, Issues,
Hotpaths, and Pathfinding sections in a resizable window. Dense rows scroll
independently. The Runtime retains the formatted startup result and appends the
latest published runtime snapshot when requested. An open window polls the text
contract at most every 500 ms. A closed window does no polling or formatting
work.

Value refreshes preserve the current section's scroll offset. Switching sections
or losing the selected section resets it; content shrinkage and window resizing
clamp it to the remaining scroll range instead of jumping to the top.

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
overlap. Queue delay is still measured in game ticks, but TPS is independently
computed from completed ticks over a roughly one-second wall-clock window.
Large game-clock jumps do not inflate that rate, and paused windows report zero.
The target-repetition tracker measures reuse candidates, not paths that are
already proven interchangeable. Its key intentionally excludes the start cell
but includes the map, target identity, and end mode. Constraint distributions
must be used to decide which candidates can safely share a path or corridor.
The scheduler snapshot reports configured workers and queued main-thread work,
not measured utilization. Expensive texture method transpilers and per-stage
process sampling are intentionally outside the always-on telemetry path.

Open diagnostics work is tracked in the [TODO](../TODO.md). Benchmark operation
and reproducibility rules are documented in
[Development and verification](development.md).
