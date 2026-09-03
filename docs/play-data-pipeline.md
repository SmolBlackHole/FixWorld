# Play-data loading stages

Parent: [Documentation index](README.md)

RimWorld owns and executes `PlayDataLoader.DoPlayLoad()`, including its original
mod ordering, XML and Def processing, and deferred main-thread work list.
FixWorld observes method boundaries without skipping or reordering those
operations.

## Stage model

The Runtime exposes 17 technical stages grouped into four UI phases:

```text
Boot
  01 Reset play data
  02 Initialize mods

Content
  03 Initialize texture cache
  04 Index texture sources
  05 Prepare mod content
  06 Create mod classes

Definitions
  07 Load and patch XML
  08 Import definitions
  09 Early binding
  10 Generate pre-resolve definitions
  11 Resolve cross-references
  12 Resolve definitions
  13 Generate post-resolve definitions
  14 Finalize definitions

Finalize
  15 Initialize runtime
  16 Execute deferred main-thread work
  17 Complete
```

Stages 03 and 04 are the only FixWorld-owned work in this sequence. They open
the DDS pack cache and index effective texture sources after RimWorld has
initialized the active mod list. All other stages measure elapsed time between
stable RimWorld calls. Stage 16 starts when RimWorld's
`ExecuteWhenFinished()` list becomes ready. FixWorld iterates that same list in
RimWorld's original order through its existing long-event enumerator path. This
allows a frame at least every 100 ms between actions without copying delegates or
reconstructing closures. A single long-running action can still block one frame.

The boundaries intentionally describe useful phases rather than individual
method timings. For example, `Load and patch XML` includes RimWorld's discovery,
merge, TKey, validation, and patch operations. FixWorld does not persist combined
XML or finished Def objects.

## Measurement behavior

The Runtime telemetry store uses pre-registered shared profiler slots to retain
wall time, call count, and failure count for every stage. The loading UI reads
the current state directly while the diagnostics window retains all 17 completed
rows.

The hooks do not call `SetCurrentEventText()`, alter mod order, copy delegates, or
reconstruct closures. Per-action exceptions retain RimWorld's log-and-continue
behavior. Other exceptions remain under RimWorld's normal recovery logic.
FixWorld aborts only its current telemetry session and starts a fresh one if
RimWorld retries the load.

Open measurement and DDS work is tracked in the [TODO](../TODO.md). Raw selected
benchmarks remain in `data/benchmarks`.
