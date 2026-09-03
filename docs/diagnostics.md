# Runtime diagnostics

Parent: [Documentation index](README.md)

The Runtime owns one telemetry store for startup diagnostics. The Loader and
normal Mod expose its output at their boundaries; they do not maintain parallel
profiling systems.

## Data flow

Passive RimWorld hooks publish typed stage events. The Runtime telemetry store
consumes those events and creates one immutable schema 18 snapshot containing:

- the preloader timeline and DDS read-ahead result;
- all 17 loading-stage timings;
- process CPU, managed-heap, working-set, and GC deltas per stage;
- texture and DDS cache counters;
- configured scheduler workers, pending main-thread actions, and system memory.

The snapshot is the source for all three outputs:

1. a compact RimWorld log summary;
2. versioned JSON consumed by `tools/benchmark.py` when benchmarking is enabled;
3. formatted read-only text exposed through the Runtime contract to the Mod UI.

Python validates the JSON schema, writes `loader-stages.csv`, and appends a
selected aggregate result row. It does not reconstruct timings from the RimWorld
log.

## In-game UI

The normal Mod presents Startup, Preloader, Stages, DDS and textures, Runtime,
and Issues sections in a resizable window. Dense stage rows scroll independently.
The Runtime formats the retained snapshot once; an open window polls the stable
text contract at most every 500 ms. A closed window does no polling or formatting
work.

The UI is observational. Opening it does not install Harmony patches, activate
additional profilers, change scheduler behavior, or mutate the completed
snapshot.

## Current limits

Stage timings show where startup time is spent, but they do not attribute nested
work to individual mods or methods. The scheduler snapshot reports configured
workers and queued main-thread work, not measured utilization. Detailed texture
capture is enabled for benchmark runs and is identified explicitly in the
snapshot.

Open diagnostics work is tracked in the [TODO](../TODO.md). Benchmark operation
and reproducibility rules are documented in
[Development and verification](development.md).
