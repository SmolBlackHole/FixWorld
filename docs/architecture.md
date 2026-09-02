# FixWorld architecture

Parent: [Documentation index](README.md)

FixWorld separates early process entry, version validation, runtime ownership,
and RimWorld-facing mod integration. Each process has one runtime and one active
play-data loading path.

## Boot flow

```text
RimWorldWin64.exe
  -> UnityDoorstop
  -> FixWorld.Preloader.dll
     -> wait for Assembly-CSharp
     -> preload a combined-XML cache candidate
     -> resolve the installed Harmony assembly
  -> FixWorld.Loader.dll
     -> validate RimWorld version, MVID, and required methods
     -> load FixWorld.Runtime.dll
  -> FixWorld.Runtime
     -> create runtime services
     -> claim PlayDataLoader.DoPlayLoad()
     -> validate or rebuild the combined-XML cache
     -> execute the owned play-data pipeline
  -> FixWorld.Mod.dll
     -> attach settings and ModContentPack to the running runtime
```

The normal RimWorld mod is not a second runtime. It installs the early loader on
the first launch, owns settings and UI, and attaches RimWorld-specific state to
the runtime that already exists.

## Assembly ownership

| Assembly | Responsibility |
| --- | --- |
| `FixWorld.Shared` | Assembly-neutral events, scheduling, profiling, cache snapshots, and boot contracts |
| `FixWorld.Preloader` | Earliest managed entry, Assembly-CSharp observation, Harmony resolution, and loader handoff |
| `FixWorld.Loader` | Exact RimWorld contract validation and one runtime start call |
| `FixWorld.Runtime` | Lifecycle, hooks, play-data stages, scheduler, telemetry store, XML and DDS caches, and loading UI state |
| `FixWorld.Mod` | Doorstop installation, settings, RimWorld UI, and runtime attachment |
| `FixWorld.Tool` | Explicit command-line wrappers for preloader maintenance, DDS cleanup, and texconv |

The preloader and loader contain no gameplay policy. The runtime is the only
owner of long-lived infrastructure. Harmony patches are thin translation
boundaries and do not own domain state.

## Play-data pipeline

FixWorld replaces `PlayDataLoader.DoPlayLoad()` with one ordered pipeline that
currently exposes 16 stages:

```text
Reset
Initialize mods
Index mod content
Prepare mod content
Create mod classes
Load and patch XML
Import definitions
Early binding
Pre-resolve implied definitions
Cross-reference resolution
Reference resolution
Post-resolve implied definitions
Definition finalization
Initialize runtime
Execute deferred main-thread work
Complete
```

Owning the order does not mean every stage has been reimplemented. Some stages
still call the corresponding RimWorld operation through a narrow adapter. Each
deeper cutover must preserve the active mod list, ordering, Harmony expectations,
and produced game data.

## Threading boundary

Unity and mutable Verse state remain on the main thread. Workers may prepare
only independent file, hash, cache, or byte data. Worker results are immutable
and must be committed in deterministic order through the main-thread queue.

The stage pipeline owns ordering and barriers. The scheduler owns resource
limits and execution. The event bus reports typed observations. These concerns
must not be collapsed into one executor.

## Failure boundary

Before FixWorld claims `DoPlayLoad()`, contract failure disables the early
runtime and lets RimWorld continue through its original loader. After ownership
has been claimed, an unexpected failure is reported and terminates that load
instead of silently executing a second pipeline.

Doorstop installation, repair, and removal are documented separately in the
[Windows loader guide](windows-preloader.md).
