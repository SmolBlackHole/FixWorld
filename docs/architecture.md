# FixWorld architecture

Parent: [Documentation index](README.md)

FixWorld separates early process entry, version validation, runtime services,
and RimWorld-facing mod integration. RimWorld owns play-data execution. FixWorld
observes that execution and replaces only the texture read path on a valid DDS
cache hit.

## Boot flow

```text
RimWorldWin64.exe
  -> UnityDoorstop
  -> FixWorld.Preloader.dll
     -> stop unless FixWorld is active in ModsConfig.xml
     -> wait for Assembly-CSharp
     -> start bounded DDS read-ahead
     -> resolve the installed Harmony assembly
  -> FixWorld.Loader.dll
     -> validate RimWorld version, MVID, and required methods
     -> load FixWorld.Runtime.dll
  -> FixWorld.Runtime
     -> create runtime services
     -> install passive play-data stage hooks
     -> install the DDS texture-load detour
  -> RimWorld PlayDataLoader.DoPlayLoad()
     -> execute the original loader and deferred actions
     -> FixWorld yields deferred work through an isolated loading overlay
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
| `FixWorld.Preloader` | Earliest managed entry, Assembly-CSharp observation, DDS read-ahead, Harmony resolution, and loader handoff |
| `FixWorld.Loader` | Exact RimWorld contract validation and one runtime start call |
| `FixWorld.Runtime` | Lifecycle, passive stage observation, scheduler, telemetry, DDS cache, and loading UI state |
| `FixWorld.Mod` | Doorstop installation, settings, RimWorld UI, and runtime attachment |
| `FixWorld.Tool` | Explicit command-line wrappers for preloader maintenance, DDS cleanup, and texconv |

The preloader and loader contain no gameplay policy. The runtime owns long-lived
FixWorld services. Harmony patches are thin observation or texture-routing
boundaries and do not reproduce RimWorld's loader.

## Play-data observation

FixWorld does not replace `PlayDataLoader.DoPlayLoad()` or
`LongEventHandler.ExecuteWhenFinished()`. It records transitions at selected
RimWorld method boundaries and presents them as 17 technical stages grouped into
Boot, Content, Definitions, and Finalize. RimWorld retains the deferred action
list and its ordering. FixWorld exposes the actions through RimWorld's existing
time-sliced long-event enumerator so the loading UI can redraw. While per-mod
content reloads remain pending, FixWorld suppresses RimWorld's normal long-event
UI and draws only its already initialized overlay. This prevents the normal UI
from resolving assets against a partially reloaded content set. FixWorld does
not own mod order, XML processing, Def construction, or deferred action contents.

The authoritative stage list and measurement boundary are documented in the
[play-data pipeline](play-data-pipeline.md).

## Threading boundary

RimWorld and Unity retain their original threading behavior. FixWorld workers
are used by the DDS cache for file, hash, conversion, and pack preparation.
Unity texture creation remains on the thread that requested the texture, and
background results are committed through the main-thread queue where required.

The telemetry store owns the active startup measurement and uses reusable slots
from the shared profiler for timing aggregation. The event bus carries typed
lifecycle notifications but owns no telemetry state. The completed snapshot is
consumed by logs, benchmarks, and the read-only diagnostics UI. See
[runtime diagnostics](diagnostics.md).

## Failure boundary

If the supported RimWorld contract or runtime-hook installation cannot be
proven, the early runtime stays disabled and RimWorld continues with its original
loader. DDS initialization and lookup failures fall back to the source texture.
FixWorld never starts a second play-data pipeline after a failure.

Doorstop installation, repair, and removal are documented separately in the
[Windows loader guide](windows-preloader.md).
