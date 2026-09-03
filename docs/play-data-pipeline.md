# Play-data loading pipeline

Parent: [Documentation index](README.md)

FixWorld owns the call order of `PlayDataLoader.DoPlayLoad()`. This gives the
Runtime one deterministic orchestration root, but it does not imply that every
RimWorld operation has already been replaced.

## Stage model

The Runtime exposes 17 technical stages grouped into four UI phases:

```text
Boot
  01 Reset play data
  02 Initialize mods

Content
  03 Index mod content
  04 Prepare texture cache
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

The loading UI displays the four phases while retaining the active technical
stage. Runtime telemetry records every technical stage and the measured RimWorld
operations inside stages that still delegate work.

The immutable mod-file snapshot is built once. It serves normal content lookup
and ordered assembly lookup and reproduces RimWorld's assembly precedence. Def
and Patch discovery deliberately remains at the XML boundary instead of being a
second competing file loader.

## Combined XML cache

The preloader may parse a combined-XML cache candidate before the Runtime needs
it. The Runtime validates the candidate after `CreateModClasses()`, when mods have
had an opportunity to create or change files.

The cache identity covers the RimWorld build, active mods in effective order,
resolved load folders, effective Def XML files, sizes, and modification times.
The schema 2 artifact contains only this identity, the parsed `XmlDocument`, and
node-to-source indices. It does not persist source paths or RimWorld objects.
The Runtime rebuilds the canonical source registry from the validated current
files and creates only the provenance lookup around the same `XmlDocument`
reference.

Cache reuse is disabled when Harmony patches touch the XML discovery, asset
construction, or unified-document merge operations that would otherwise be
skipped. A missing, stale, corrupt, or incompatible candidate follows RimWorld's
normal XML path and is replaced atomically after a successful load.

Three 88-mod schema 17 runs measured approximately 545 ms for `LoadModXML()`,
237 ms for `CombineIntoUnifiedXML()`, 107 ms for patch validation, 1,711 ms for
patch application, and 2,619 ms for `ParseAndProcessXML()`. Two unchanged cache
hits reduced the complete XML stage from roughly 2.45-2.59 seconds to
1.75-1.79 seconds. The preloader parse took 249-260 ms outside the Runtime's
critical path; Runtime input validation took about 42 ms.

Finished Def objects are not persisted. Their construction mutates global
registries, depends on mod-defined types, and participates in cross-reference,
DefOf, short-hash, and static-initialization behavior.

## Deferred main-thread work

FixWorld captures actions submitted through RimWorld's
`ExecuteWhenFinished()` path while play data loads. It then adds RimWorld
finalization operations and executes the resulting snapshot in original order
inside one long event on the main thread. The current runner yields after a
bounded interval so the loading UI can update, but it does not make the actions
parallel.

Each action records its inferred owner, label, execution time, failure state,
average queue delay, and maximum queue delay. Recent fully warm 88-mod captures
spend roughly 11 to 19 seconds in this stage, making it the dominant remaining
startup target. Known hotpaths include Lunar and Geological Landforms startup,
`ThingDef.PostLoad`, static atlas baking, mod content reloads, and static
constructors.

Owner inference and timing do not prove thread safety. Before moving an action
to a worker, FixWorld must identify its inputs, global side effects, ordering
dependencies, and Unity or Verse main-thread requirements. Workers may prepare
pure data, but externally visible state is committed in deterministic order.

Open implementation work is tracked in the [TODO](../TODO.md). Raw measurements
remain in `data/benchmarks` and ignored profiling captures.
