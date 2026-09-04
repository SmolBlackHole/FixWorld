# Performance Optimizer 1.6: implementation notes

## Purpose and scope

Performance Optimizer is a user-configurable RimWorld 1.6 Harmony mod. It
patches selected runtime hot paths, caches results for a configurable number of
game ticks or Unity frames, throttles selected work, and optionally hides or
reduces parts of the play UI. It does not replace RimWorld's mod loader or
content pipeline, and it does not persist game objects or pre-process mod
assets.

The decompiled project targets `net472` and references `Assembly-CSharp`,
Unity modules, Harmony, and Allow Tool in
[`PerformanceOptimizer.csproj`](../../decompiled/third-party/PerformanceOptimizer-1.6/PerformanceOptimizer.csproj).
The corresponding local RimWorld reference is the 1.6 assembly recorded in
[`decompiled/provenance.md`](../../decompiled/provenance.md), not necessarily
the current installed build. This document describes observed source behavior,
not a claim that every optimization is safe for every mod.

## Architecture and initialization

The mod is centered on four cooperating pieces:

- [`PerformanceOptimizerMod`](../../decompiled/third-party/PerformanceOptimizer-1.6/PerformanceOptimizer/PerformanceOptimizerMod.cs)
  owns the Harmony instance, settings object, current `TickManager`, and a
  `DontDestroyOnLoad` Unity `GameObject` hosting the per-frame patch worker.
- [`Optimization`](../../decompiled/third-party/PerformanceOptimizer-1.6/PerformanceOptimizer/Optimization.cs)
  is the patch/setting lifecycle base class. Each concrete optimization
  declares its category, label, default state, and `DoPatches()` implementation.
- [`PerformanceOptimizerSettings.Initialize`](../../decompiled/third-party/PerformanceOptimizer-1.6/PerformanceOptimizer/PerformanceOptimizerSettings.cs)
  reflects over all non-abstract subclasses of `Optimization`, creates missing
  settings entries, categorizes them, and calls `Apply()` on every entry.
- [`InitializeMod`](../../decompiled/third-party/PerformanceOptimizer-1.6/PerformanceOptimizer/InitializeMod.cs)
  delays that initialization until RimWorld has run its static constructors.
  If BetterLoading is active, it targets BetterLoading's
  `CreateTimingReport`; otherwise it targets
  `StaticConstructorOnStartupUtility.CallAll`.

The `PerformanceOptimizerMod` constructor calls `Harmony.PatchAll()` first, then
adds a prefix for static-data reset to a set of game/map lifecycle methods:
`MapDeiniter.Deinit`, `Game.AddMap`, component filling/finalization methods,
`Game.InitNewGame`, `Game.LoadGame`,
`GameInitData.ResetWorldRelatedMapInitData`, and
`SavedGameLoaderNow.LoadGameFromSaveFileNow`. The reset prefix updates the
current tick manager and calls every optimization's `Clear()` method. This is
the main lifetime boundary for in-memory caches.

Settings are serialized with RimWorld's `Scribe_Collections` in
`PerformanceOptimizerSettings.ExposeData()`. New optimization classes are
added automatically on the next initialization. Settings are displayed in
scrollable UI sections for UI tweaks, performance/misc tweaks, and throttles.

## Common cache and patch mechanics

`Optimization_RefreshRate` adds a serialized integer refresh rate and writes it
to a concrete optimization's static `refreshRateStatic` field using
`AccessTools.Field`. `CachedValueTick<T>` and `CachedObjectTick<T>` hold one
value plus an expiry tick. On a cache hit, the Harmony prefix writes the cached
value to `__result` and returns `false`; on expiry, the original method runs and
the postfix stores the new result. `CachedValueUpdate<T>` provides the same
pattern against `Time.frameCount` for UI data.

The implementation generally uses a `Dictionary` keyed by the receiving
object, a stable object ID, or a manually composed integer hash. Every concrete
optimization has an explicit `Clear()` that empties its cache. There is no
global eviction, weak-reference policy, lock, or dependency-driven
invalidation. A cache is therefore deliberately approximate: correctness
depends on the chosen refresh interval and any extra invalidation hooks.

`Optimization.Apply()` is also the enable/disable boundary. Enabling calls
`DoPatches()` once when no methods are recorded; disabling calls `UnPatchAll()`
for the methods stored in `patchedMethods`. Patch failures are logged by the
base helper, while several discovery and parsing paths intentionally swallow
exceptions.

## Optimization mechanisms by subsystem

### Pawn, health, mood, ideology, and gameplay queries

The following patches use the generic tick cache described above. Defaults are
the refresh interval in game ticks:

- `CompOverseerSubject.CanGoFeral`, 30 ticks.
- `PawnUtility.IsTeetotaler`, 500 ticks.
- `InvisibilityUtility.IsPsychologicallyInvisible`, 60 ticks, with an explicit
  `HediffComp_Invisibility.UpdateTarget` hook removing the affected pawn's
  cache entry.
- `Pawn_InteractionsTracker.CurrentSocialMode`, 30 ticks.
- `MentalBreaker.BreakThresholdExtreme`, 300 ticks.
- `MoodThresholdExtensions.CurrentMoodThresholdFor`, 30 ticks.
- `ThoughtHandler.TotalMoodOffset`, 500 ticks.
- `SkillRecord.LearnRateFactor`, 300 ticks.
- `Need_Beauty.CurrentInstantBeauty`, 600 ticks.
- `QuestUtility.IsQuestLodger`, 30 ticks.

`IdeoUtility.GetStyleDominance` is cached for 4,000 ticks using a hash of
`Thing.thingIDNumber` and `Ideo.id`. `PlantFallColors.GetFallColorFactor` uses a
hash of latitude and day-of-year and also refreshes every 4,000 ticks. These
keys assume the inputs are sufficient to identify the result.

Two one-time caches cover stable definition queries:

- `HediffDef.PossibleToDevelopImmunityNaturally` caches by `shortHash`.
- `StatWorker_MarketValue.CalculableRecipe` caches by `BuildableDef`.

The one-time caches have no per-input invalidation and are cleared only at the
global lifecycle boundaries.

### Designation, construction, gizmo, and interaction UI

`BuildCopyCommandUtility.FindAllowedDesignator` caches `Designator_Build` by
`BuildableDef` for 120 ticks. `Designator.CreateReverseDesignationGizmo`
uses a manually combined hash of designator hash code and thing ID, refreshing
every 30 ticks.

`GizmoGridDrawer.DrawGizmoGridFor` is transpiled so `ISelectable.GetGizmos()`
goes through `GetGizmosFast`. It caches materialized `List<Gizmo>` values for 30
Unity frames and resets related caches when the generated gizmo state is
processed. When Allow Tool is active, the cached list is still passed through
`ModCompatUtility.ProcessAllowToolToggle`, which can mutate command state.

`ForbidUtility.IsForbidden` is cached only while
`JobDriver.CheckCurrentToilEndOrFail` is running. Its cache is nested by pawn
ID and `Thing`, which limits the semantic scope of the optimization. The
colonist bar's `DrawIcons` stores copied icon draw-call lists for 30 ticks and
replays those draw calls. `Pawn.DrawPos` is cached for 10 ticks only while
specific consumers are executing: Dubs Mint Minimap's `DrawAllPawns` or
`Designation.Draw`. Outside those consumers the original property is used.

### World simulation and rendering

- `Frame.WorkToBuild` is cached for 60 ticks.
- `GenCelestial.CurCelestialSunGlow(Map)` is cached for 60 ticks.
- `PawnCollisionTweenerUtility.PawnCollisionPosOffsetFor` is cached for 30
  ticks.
- `Plant.TickLong` is throttled to 6,000 ticks for a selected set of plants,
  while still calling each `ThingComp.CompTickLong()` on skipped invocations.
  A transpiler replaces literal `2000` tick/float intervals with a conditional
  value, preserving 2,000 for plants outside the selected set.
- `Precept_RoleSingle.RecacheActivity` and `Precept_RoleMulti.RecacheActivity`
  are throttled to 30 ticks. A temporary flag disables throttling while
  `Ideo.RecacheColonistBelieverCount` runs.
- `JobGiver_ConfigurableHostilityResponse.TryGiveJob` is throttled per pawn to
  30 ticks.
- `WindManager.WindManagerTick` receives a transpiler early return when
  `Prefs.PlantWindSway` is disabled.

The refresh-rate patches trade freshness for fewer repeated calculations. They
do not move work off the main thread, and they do not make Unity API calls
thread-safe.

### Optional UI and sound changes

The UI tweak classes are disabled by default except `Optimization_UIToggle`.
They can suppress or replace expensive UI paths:

- `AlertsReadout.AlertsReadoutOnGUI` draws a reduced alert list away from the
  right edge.
- `GlobalControlsUtility.DoPlaySettings` and `DoTimespeedControls` hide parts
  of the bottom-right controls while preserving key handling.
- `MainButtonsRoot.DoButtons` suppresses the bottom button bar and optionally
  patches UI Not Included's bar methods when that mod is installed.
- `ResourceReadout.ResourceReadoutOnGUI` draws only while the mouse is over the
  resource area.
- `Optimization_UIToggle` toggles these UI behaviors from a configured key
  binding and persists the toggle state.
- `Optimization_DisableSounds_Update` is an opt-in hard disable for sound
  updates, one-shot playback, and mouseover sound resolution.

`Optimization_UIToggle` uses `MapInterface.HandleMapClicks` as its post-fix
input point. This is a useful compatibility detail: it avoids replacing the
whole GUI loop, but means the toggle depends on map-click processing running.

## Asynchronous work and concurrency

The broadest transformation is `Optimization_FasterGetCompReplacement`. It
reflects over `GenTypes.AllTypes`, filters assemblies/types/method names, reads
original IL, and locates generic calls such as `GetComponent<T>()`,
`GetCompProperties<T>()`, `CompProps<T>`, and `TryGetComp<T>`. It maps those
calls to generic fast-cache methods in `ComponentCache`, including map, world,
game, world-object, hediff, thing-def, and hediff-def lookups.

The initial IL scan runs in `Task.Run` from `DoPatchesAsync`. After scanning,
the discovered transpiles are placed on `PerformPatchesPerFrames`, a persistent
Unity component. Its coroutine applies at most approximately one millisecond
of transpiling work before yielding to the next frame. This keeps the game
loop responsive during patch installation, but patch application itself still
runs on Unity's main thread. The async method is `async void`, failures in the
background scan are swallowed, and the shared dictionaries are not
thread-safe. The implementation therefore optimizes startup responsiveness,
not general parallel execution of RimWorld work.

`ComponentCache` uses closed generic static dictionaries. It caches components
by IDs, definitions by short hashes, map components by `Map`, and current
world/game components by identity. `GetWorldComponentFast` and
`GetGameComponentFast` retain the last owner and component pair. All cache
families are cleared by reflection-generated closed generic `Clear` methods in
`Optimization_FasterGetCompReplacement.Clear()`.

## Profiling, logging, and compatibility behavior

When a patch has a boolean prefix, `Optimization.Patch` can add instrumentation
around it if `ProfilePerformanceImpact` is enabled. `MeasureBefore` starts one
static stopwatch and alternates between profiling the optimization enabled and
disabled every 250 game ticks. `MeasureAfter` stores elapsed samples by
optimization type and eventually logs paired totals and ratios as SUCCESS or
FAIL. This is an A/B measurement mechanism, not a low-overhead always-on
telemetry store. It uses static mutable state and lists, and assumes calls are
serialized.

`Watcher` is a disabled development optimization that patches the play GUI and
collects one-second FPS/TPS samples per `TimeSpeed`, rendering an immediate
diagnostic window. `Stats` and `Logging` provide additional ad-hoc log helpers.
`Log_Error_Patch` temporarily suppresses `Log.Error(string)` while settings are
deserialized to tolerate stale deep-serialized optimization entries.

The mod has explicit compatibility work for BetterLoading, Dubs Performance
Analyzer, Allow Tool, Dubs Mint Minimap, UI Not Included, and selected classes
excluded from the generic IL scan. Most compatibility decisions are name- or
type-based. Unknown methods, failed type loads, and many IL parsing errors are
silently skipped. This makes installation resilient, but can also hide a
partially applied optimization set.

## Reusable lessons for FixWorld

1. **Separate measurement from activation.** The per-optimization lifecycle,
   serialized enable state, and patch ownership in `Optimization` are a useful
   model for measured, individually reversible FixWorld experiments.
2. **Prefer explicit invalidation where semantics change.** The invisibility
   and gizmo hooks show that a timer alone is not enough for stateful results.
   FixWorld caches should expose a narrow invalidation contract for mod/content
   changes rather than relying only on a global reset.
3. **Use consumer-scoped caching.** `Pawn.DrawPos` and `IsForbidden` reduce risk
   by enabling caching only at known call sites or call contexts. This is safer
   than globally changing a frequently used API.
4. **Keep Unity work on the main thread, but prepare metadata off-thread.** The
   per-frame transpiler worker demonstrates a responsive scheduling boundary;
   it is not evidence that arbitrary RimWorld or Unity work can be moved to
   worker threads.
5. **Avoid broad automatic IL rewrites in the stable runtime.** The generic
   replacement scan has a large exclusion list, silent failure paths, and
   shared mutable state. It is appropriate as an opt-in experiment or
   build-time analysis, not as an invisible compatibility layer without per-method
   verification.
6. **Do not assume every cached result is safe to replay.** Gizmo lists,
   commands, components, and definition-derived values have different
   lifetimes. A generic cache abstraction should make key identity, expiry, and
   invalidation explicit instead of hiding them behind one universal timer.
7. **Use a runtime UI for evidence, not only logs.** The settings UI and the
   optional Watcher window expose toggles and measurements in RimWorld, but the
   A/B profiler's static lists and log-only output suggest that FixWorld's
   central telemetry store should own bounded snapshots and lifecycle resets.

## Verification notes

Claims above were checked against the decompiled source files listed in each
section. No external source or binary behavior was assumed. The source is a
decompilation captured on 2026-08-30; exact method signatures and available
RimWorld types should be rechecked against the target assembly before copying
any patch into FixWorld.
