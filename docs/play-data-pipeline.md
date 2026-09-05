# Play-data loading stages

Parent: [Documentation index](README.md)

RimWorld owns and executes `PlayDataLoader.DoPlayLoad()`, including its original
mod ordering, XML and Def processing, and deferred main-thread work list.
FixWorld observes method boundaries without skipping or reordering those
operations.

## Stage model

The fork exposes 15 boundaries in four UI phases. DDS stages are absent until
that feature is restored. Unobserved work before early attachment is not timed.

```text
Boot
  01 Reset play data
  02 Initialize mods

Content
  03 Prepare mod content
  04 Create mod classes

Definitions
  05 Load and patch XML
  06 Import definitions
  07 Early binding
  08 Generate pre-resolve definitions
  09 Resolve cross-references
  10 Resolve definitions
  11 Generate post-resolve definitions
  12 Finalize definitions

Finalize
  13 Initialize runtime
  14 Execute deferred main-thread work
  15 Complete
```

Stages measure elapsed time between observed RimWorld calls. Stage 14 starts when RimWorld's
`ExecuteWhenFinished()` list starts draining. FixWorld retains the original list
and action order but exposes it through RimWorld's existing time-sliced
long-event enumerator. While a `ModContentPack.ReloadContent()` action remains
pending, RimWorld's normal long-event UI is suppressed and the menu background
plus FixWorld's initialized overlay are drawn. This lets the overlay redraw without
resolving normal UI assets against a partially reloaded content set. Actions run
in their original order with a frame opportunity at least every 100 ms. A single
long-running action can still block one frame.

The boundaries intentionally describe useful phases rather than individual
method timings. For example, `Load and patch XML` includes RimWorld's discovery,
merge, TKey, validation, and patch operations. FixWorld does not persist combined
XML or finished Def objects.

## Measurement behavior

`LoadingProgress` publishes immutable stage snapshots into the shared telemetry
store. UI, log and JSON use that contract, including stage durations and failure
text. The screen reads its current state without publishing a snapshot each frame.
Mod constructors' private XML-resolution calls cannot advance the global binding
stage before the main XML import begins.

The hooks do not call `SetCurrentEventText()`, alter mod order, copy delegates,
or reconstruct closures. Per-action exceptions retain RimWorld's
log-and-continue behavior. Other exceptions remain under RimWorld's normal
recovery logic. FixWorld aborts only its current telemetry session and starts a
fresh one if RimWorld retries the load.

Open measurement and DDS work is tracked in the [TODO](../TODO.md). Raw selected
benchmarks remain in `data/benchmarks`.
