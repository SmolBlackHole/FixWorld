# Dubs Performance Analyzer: source review

This note documents the 1.6 source under `tools/dubs-performance-analyzer`. It describes what the analyzer actually does, rather than treating it as a general-purpose profiler or as a replacement for RimWorld's runtime. The repository also contains 1.5 and older compiled assemblies, but the source project reviewed here is `Source/Dubs Performance Analyzer.1.6.csproj`.

## Purpose and scope

Dubs Performance Analyzer (DPA) is an in-game, opt-in diagnostic mod for finding expensive work in three runtime categories:

- **Tick**: work executed from `TickManager.DoSingleTick`, including pawn, thing, job, pathfinding, stat, room, and world-pawn paths.
- **Update**: frame/update work outside the tick category, including root, UI, map drawing, game components, and Harmony/transpiler diagnostics.
- **GUI**: selected `OnGUI` paths such as the UI root, window stack, colonist bar, overlays, resource readout, and game-component GUI.

It also provides a **Modder** category populated from a mod's optional `Analyzer.xml`, a settings panel, an in-game analyzer window, method-level/internal-method profiling, stack traces, Harmony-patch inspection, and recording/comparison of profiling samples. The built-in list is not a permanent always-on profiler: the main measurement patches are installed when `Window_Analyzer.PreOpen` runs and are removed asynchronously after the window closes (`Window_Analyzer.PostClose`, `GameComponent_Analyzer.GameComponentUpdate`, and `Analyzer.Cleanup`).

The README explicitly positions the tool as a diagnostic aid for distinguishing average cost from spikes and for locating the mod/assembly behind a slow method. It warns that stack traces and profiling patches add overhead, that exceptions can invalidate timings, and that transpiler attribution is approximate (`README.md`, “Reading the Display”, “Exceptions”, and “Transpiler Profiling”).

## Architecture and bootstrapping

### Mod initialization

`Source/Modbase.cs`, class `Analyzer.Modbase`, is the mod entry point. Its constructor:

1. Loads `Settings` through RimWorld's `GetSettings`.
2. Creates two Harmony instances:
   - `Dubwise.PerformanceAnalyzer` (`StaticHarmony`) for infrastructure and always-running patches.
   - `Dubwise.DubsProfiler` (`Harmony`) for temporary profiling patches.
3. Optionally integrates with Visual Exceptions by reading its configuration through reflection.
4. Installs Harmony-ID capture patches (`RememberHarmonyIDs`) unless Visual Exceptions supplies that integration, and optionally instruments HugsLib's `ApplyHarmonyPatches`.
5. Builds the assembly-to-mod lookup with `ModInfoCache.PopulateCache`, creates the category tabs with `GUIController.InitialiseTabs`, reads all active mods' `Analyzer.xml` files through `XmlParser.CollectXmlData`, and initializes stack-trace support.
6. Installs always-running patches for the TPS/FPS readout and debug-log/dev-mode helpers.

The main button is declared in `Defs/MainButton.xml` as `DubsOptimizer`, uses worker `Analyzer.MainButton_Toggle`, and points at the `DPA/UI/MintSearch` icon. `MainButton_Toggle.Activate` opens/closes `Window_Analyzer`; right-click exposes a cleanup command when temporary patches are installed. `Source/KeyBindings.cs`, class `H_KeyPresses`, additionally patches `UIRoot_Entry.UIRootOnGUI` and `UIRoot_Play.UIRootOnGUI` to handle the analyzer and restart key bindings.

### Lazy activation and cleanup

`Window_Analyzer.PreOpen` loads entries once (`LoadEntries`), sets `Analyzer.BeginProfiling`, and installs the two sampling boundary patches:

- `Profiling.H_RootUpdate` around `Root_Play.Update`.
- `Profiling.H_DoSingleTickUpdate` around `TickManager.DoSingleTick`.

It then reapplies saved custom patches. `PostClose` turns profiling off, returns the UI to settings, persists settings, and schedules cleanup after 30 seconds unless disabled. `GameComponent_Analyzer.GameComponentUpdate` cancels that countdown if the analyzer is reopened, otherwise calls `Analyzer.Cleanup` when it expires. Cleanup runs on a task, unpatches the temporary Harmony ID, clears method/transpiler caches, clears profiles and pinned handles, clears logs and generated entries, and explicitly invokes GC. This delayed teardown explains the README warning that reopening immediately after closing can be temporarily refused.

## Profiling and sampling data flow

The runtime path is deliberately split into a cheap recording phase and a less frequent calculation phase.

### Measurement boundaries

`Profiling.Statistics.ProfileController.BeginUpdate` starts a root `Stopwatch` and marks an update as active. `EndUpdate` stops it, calls `Analyzer.UpdateCycle`, accumulates `Time.deltaTime`, and invokes `Analyzer.FinishUpdateCycle` once the configured interval elapses. The default `Settings.updatesPerSecond` is 2, so statistic calculation is normally requested twice per second, while the underlying update boundary is every frame or every tick depending on the selected category.

`H_RootUpdate` measures a non-tick update around `Root_Play.Update`. `H_DoSingleTickUpdate` uses `TickManager.DoSingleTick` as the update boundary when the selected category is Tick. This distinction is important: multiple ticks can occur in one rendered frame, so tick samples and frame/update samples have different units. `README.md`, “Technical Explanations / Tick Vs Update Methods”, documents the same model.

### Per-key storage

`Profiling.Statistics.ProfileController.Start` first checks `Analyzer.CurrentlyProfiling`, then obtains or creates a `Profiler` in a `ConcurrentDictionary<string, Profiler>`, and starts its stopwatch. `Stop` stops the matching profiler. `Profiler` stores two fixed arrays of length `RECORDS_HELD = 2000`: elapsed milliseconds and call counts. `RecordMeasurement` writes one slot per update/tick boundary and advances a ring-buffer index. Thus the hot path reuses bounded storage rather than allocating one object per call, but it still uses a `Stopwatch` per logical profile and a dictionary lookup for ordinary dynamic profiles.

`Analyzer.UpdateCycle` asks every active `Profiler` to commit its current stopwatch and hit count to the ring buffer. `Analyzer.FinishUpdateCycle` copies the profile dictionary and starts `ProfileCalculations` on a task. The worker computes average, maximum, total, calls, and percentage-of-update values, sorts using the selected comparer, and atomically replaces the displayed `logs` list under `LogicLock`. The UI therefore reads a snapshot of calculated logs instead of doing the aggregation in `DoWindowContents`.

`Profiling.Statistics.LogStats` separately copies the selected profiler's arrays and calculates totals, means, medians, and maxima on a task for the lower statistics panel. `Panel_Save` can consume the ring-buffer samples into a bounded file format via `FileUtility`, and can compare two saved entries using the same summary statistics.

### Method instrumentation

Each profiling entry is a class marked with `[Entry(name, Category)]`. `Window_Analyzer.LoadEntries` discovers these types through `GenTypes.AllTypes`, reads `[Setting]` fields, and puts entries into `GUIController` tabs. When a user selects an entry, `Entry.PatchMethods` calls the entry type's `ProfilePatch` or `GetPatchMethods` through `Analyzer.PatchEntry`.

`Profiling.Utility.ProfilingUtility.MethodTransplanting` is the main instrumentation mechanism:

- It applies a Harmony transpiler to selected methods.
- The transpiler inserts an active/paused check, creates or retrieves a keyed `Profiler`, and starts/stops it at each `ret` instruction.
- For internal-method profiling, `ReplaceMethodInstruction` creates a `DynamicMethod` wrapper around a called method and measures that call only when reached from the selected parent.
- For transpiler profiling, `TranspilerMethodUtility` and the Myers diff utility compare original/current IL and wrap calls introduced by transpilers. The README notes that this can misattribute or fail for added control-flow/exception constructs.
- Dynamic or pinned profile references are retained through `GCHandle` in `ProfileController.Handles`; cleanup frees these handles after unpatching.

The code intentionally embeds much of the fast path in generated IL. `MethodTransplanting.Transpiler` directly accesses the active flags and profiler dictionary instead of calling a high-level helper for every instrumented method. Even so, a selected entry can patch many methods and materially change runtime cost.

## Built-in profiling surfaces

The `[Entry]` inventory in the 1.6 source contains 19 Tick entries, 17 Update entries, and 7 GUI entries. The concrete classes are under `Source/Profiling/Patches/{Tick,Update,GUI}`. Representative groups include:

- Tick: `H_TickListTick`, `H_PawnTickProfile`, `H_JobGivers`, `H_JobDriver`, `H_WorkGivers`, `H_FindPath`, `H_GetStatValue`, `H_NeedsTrackerTick`, `H_ThinkNodes`, `H_ThingComps`, `H_MapComponentTick`, and `H_WorldPawns`.
- Update: `H_Root`, `H_UIRootUpdate`, `H_GameComponentUpdate`, `H_MapComponentUpdate`, `H_DrawSection`, `H_DrawDynamicThings`, `H_SectionLayer_Things`, `H_SectionLayer_ThingsDrawLayer`, `H_RenderPawnAt`, `H_InfoCard`, `H_Shooting`, and Harmony/transpiler entries.
- GUI: `H_UIRootOnGUI`, `H_WindowStackOnGUI`, `H_GameComponentOnGUI`, `H_ColonistBarOnGUI`, `H_ResourceReadoutOnGUI`, `H_ThingOverlaysOnGUI`, and `H_DoTabs`.

The Tick Things entry (`H_TickListTick`) is more invasive than simple boundary timing: it replaces `TickList.Tick` with a prefix that iterates the selected tick list and wraps each thing's `Tick`, `TickRare`, or `TickLong`. It supports grouping by type/def and filtering to selected things, which is useful diagnostically but changes the exact execution path and exception handling. This is a diagnostic patch, not a drop-in optimization.

## UI integration

`Source/Window_Analyzer.cs` owns the RimWorld `Window` shell. It is deliberately non-modal (`forcePause = false`, `absorbInputAroundWindow = false`), draggable/resizable, and initially 890 x 650. The content is split into:

- `Panel_Tabs`: collapsible category and entry list with its own scroll view.
- `Panel_TopRow`: pause/resume, reset, text filter, update duration, FPS and TPS display.
- `Panel_Logs`: scrollable rows, configurable columns, sorting, filtering, percentage bars, pinning, and context menus.
- `Panel_BottomRow`: selected-profile graph, summary statistics, Harmony patches, stack traces, and save/compare panels.

`GUIController` is the state coordinator. It tracks the selected category, tab, entry, and profiler; resets profilers when selection changes; creates runtime-generated entries for custom type/method/assembly searches; and removes closable entries during cleanup. `Panel_Logs` reads `Analyzer.Logs` under `Analyzer.LogicLock`, while `Analyzer.FinishUpdateCycle` swaps the calculated list under the same lock.

The UI also exposes operations that alter instrumentation: selecting a log shows details, Ctrl-click invokes the entry's action, right-click can pin/unpin, launch internal-method profiling, copy a signature, save a method to custom Tick/Update, or, with Shift, unpatch methods. `Panel_BottomRow` resolves declaring assembly and mod name from Harmony patch information and the `ModInfoCache` mapping. It can open a method in dnSpy if a configured executable path exists, or open a GitHub code search URL.

## Concurrency and performance characteristics

The analyzer uses background tasks in several places:

- patching selected methods (`Analyzer.PatchEntry`, unless `Settings.disableThreadedPatching` is true);
- patching each method from `MethodTransplanting.UpdateMethod`;
- aggregating profiles (`Analyzer.FinishUpdateCycle`);
- calculating selected-profile summary statistics (`LogStats.GenerateStats`);
- cleanup/unpatching (`Analyzer.Cleanup`).

The main thread still owns Harmony patch installation semantics and all RimWorld/Unity UI calls in practice, but DPA requests patch and analysis work via `Task.Factory.StartNew`. The code does not provide a dedicated scheduler, cancellation token, bounded queue, or explicit task join for these operations. The UI uses locks/snapshot copies to avoid reading a list while it is replaced, and `ConcurrentDictionary`/`ConcurrentBag` for profile/handle registration.

The main runtime overhead comes from the selected instrumentation, not from the display alone: Harmony-transpiled methods execute active checks and stopwatch operations, and dynamic profiles can perform dictionary lookups and first-use allocation. Stack trace capture is explicitly documented as significantly slower. The fixed 2,000-sample ring buffer bounds retained sample memory, but `ProfileCalculations` creates a dictionary copy and new log lists at each calculation interval. Cleanup intentionally performs a full GC, which can cause a noticeable pause after profiling is closed even though the cleanup work is launched on another task.

## Compatibility and safety tradeoffs

Observed compatibility boundaries in the source are:

- The project targets RimWorld through `Krafs.Rimworld.Ref` and Harmony 2.3.6, with separate 1.5 and 1.6 project configurations. Entry methods are resolved at runtime via Harmony `AccessTools`, so missing/renamed game methods can fail an entry rather than being compile-time checked against every supported game build.
- Harmony-ID tracking relies on patching Harmony construction and, optionally, HugsLib. It uses stack inspection and reflection to associate an ID with an assembly. Duplicate IDs and packaged dependency DLLs can make attribution wrong; `StackTrace.cs` logs those cases.
- Assembly-to-mod mapping is based on currently running mods and loaded assembly full names (`ModInfoCache`). A dependency DLL packaged inside another mod may be attributed to the containing mod or not found.
- Timing around exceptions is not exception-safe in the ordinary transpiler path. If a profiled method throws before a generated stop path, the `Stopwatch` may remain running and the measured value is invalid. The README explicitly rejects adding try/catch/finalizer overhead globally.
- Transpiler/internal profiling rewrites IL and creates dynamic wrappers. Branches, exception blocks, unusual stack shapes, by-ref/value-type calls, or methods that do not expose expected metadata can be unsupported or misleading.
- The Tick Things diagnostic prefix reproduces part of RimWorld's tick loop. It is useful for attribution but has a larger semantic and compatibility surface than a passive timing wrapper.
- The analyzer's own Harmony ID is excluded when presenting external patch information. Cleanup unpatches only `Dubwise.DubsProfiler`, leaving the static infrastructure Harmony instance in place.

## Reusable lessons for FixWorld

1. **Separate collection from presentation.** DPA's ring buffers record bounded raw samples, a background worker derives display logs, and the UI consumes an atomic snapshot. FixWorld can use the same ownership split for stage/deferred/DDS diagnostics without having UI code calculate metrics.
2. **Keep hot-path instrumentation opt-in.** The analyzer installs broad method patches only after the user opens it, and it removes them after a quiet period. A runtime profiler should have an explicit low-overhead default and a clearly bounded detailed mode.
3. **Use fixed-size sample storage.** The 2,000-slot ring buffer is simple and prevents unbounded per-call history. A generic shared telemetry store could preserve this property while accepting stage counters and durations as well as method timings.
4. **Define sample units explicitly.** Tick, frame/update, and GUI work are not interchangeable. FixWorld should label whether a duration is per stage, per frame, per tick, per deferred item, or total wall-clock time before exposing it in the UI.
5. **Publish snapshots instead of sharing mutable collections.** DPA copies profile dictionaries before calculation, then swaps the rendered list under a lock. A versioned immutable/read-only snapshot would make this boundary clearer and reduce UI contention.
6. **Treat attribution as best-effort.** Assembly names, Harmony IDs, and mod ownership can be ambiguous. Diagnostics should expose the evidence and mark unresolved attribution rather than presenting a guessed mod as fact.
7. **Do not infer optimization from instrumentation.** DPA demonstrates where time is spent, but its Harmony transpilers and Stopwatch calls alter the measured path. FixWorld should compare instrumented and uninstrumented runs and keep measurement patches separate from production optimizations.
8. **Make teardown safe and observable.** DPA's delayed cleanup avoids leaving patches active forever, but asynchronous cleanup and full GC create a visible lifecycle. FixWorld's profiler should expose active/cleaning states and avoid using background tasks for Unity-owned operations.
9. **Use diagnostic configuration as a compatibility boundary.** `Analyzer.xml` lets a mod opt into profiling specific methods/types without DPA hard-coding every target. FixWorld can offer a similarly narrow extension point for custom counters or stage labels, while validating methods and keeping the extension disabled by default.
10. **Prefer one instrumentation boundary for repeated work.** DPA gets fine-grained visibility by wrapping the existing call boundary (`TickList.Tick`, `Pawn.Tick`, `WorkGiver`, etc.) instead of rebuilding the entire game loop. For FixWorld, this supports measuring concrete RimWorld methods while leaving ownership of the normal mod/content pipeline intact.

## Verification notes

Claims in this document were checked against the checked-in source files under `tools/dubs-performance-analyzer/Source`, `Defs/MainButton.xml`, `About/About.xml`, and `README.md`. The review was source-only; no RimWorld process was started and no DPA assembly was decompiled. The source tree contains 1.6 and 1.5 project files and compiled assemblies for several versions, but only the 1.6 source path was used for the implementation details above.
