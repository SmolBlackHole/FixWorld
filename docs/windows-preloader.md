# Windows bootstrap and restart

Parent: [Documentation index](README.md)

This documents the current HugsLib-based fork. The old Preloader -> Loader ->
Runtime chain, timeline environment-variable contract and DDS read-ahead are
archived, not dependencies of this implementation.

## Ownership

- `Bootstrap/` builds one engine-independent `FixWorld.Bootstrap.dll`.
  Doorstop targets its `Doorstop.Entrypoint.Start()`. The normal mod references
  this same assembly. `BootSession` is functional state, not telemetry.
- `FixWorldController` remains the sole owner of diagnostics, profiler and caches.
  Early entry creates those services only. Mod attachment supplies ModContentPack,
  settings, reflection bindings, library hooks and child-mod initialization.
- `Bootstrap/Installation.cs` owns disk inspection, installation and confirmation.
  The mod adapter owns when to install/restart, not a second runtime framework.
- `Bootstrap/Restart.cs` owns both sides of the restart protocol. The dedicated
  `FixWorld.Restart.exe` compiles that source and has no dependency on game DLLs.
- `Source/Patches/Restart_Patch.cs` intercepts GenCommandLine.Restart centrally.
  Mods-tab, language and quick-restart callers keep using the original method.

## First installation

The normal `FixWorldMod` constructor checks the bootstrap before initializing
the library. A cold, missing or repairable owned installation installs Doorstop
and queues the coordinated restart through ExecuteWhenFinished on the main
thread. It does not initialize the full library during this installation launch.
The static late initializer also respects this state.

Installation validates the bundled UnityDoorstop 4.4.0 SHA-256 and required files.
The game root receives only winhttp.dll, doorstop_config.ini and
FixWorld.bootstrap.json. There is no deletion of FixWorld.dll.

The versioned manifest records expected proxy/config/bootstrap hashes, bootstrap
path and restartPending. Writes are atomic per file, not a multi-file transaction.
The pending ownership record precedes writes. During repair it retains the prior
proxy/config hashes so an interrupted replacement remains identifiable.
Missing owned files are repairable. Modified/foreign files cause a conflict,
not an overwrite. A matching proxy hash alone does not prove ownership.

A pending attempt is not automatically retried after a failure. An installed
bootstrap that did not enter this process does not trigger another restart.
The user can recover explicitly; failed bootstrap activation is logged, not
silently treated as successful normal initialization.

## Subsequent launch

### Explicit installation maintenance

The diagnostics window's **Doorstop** page provides install, reinstall and
uninstall. Status inspection happens on page entry or Refresh status, not on
each frame. Each operation requires confirmation and shutdown; save the colony
first. The helper validates ownership before acknowledging the request, waits
for RimWorld to exit, validates again and applies the change. Install and reinstall
launch the game once afterward; uninstall leaves it closed for mod removal.
Foreign proxy/config files are never overwritten or removed.

Uninstall removes the owned `winhttp.dll` and `doorstop_config.ini`, then deletes
`FixWorld.bootstrap.json` last. Mod files, DDS packs, settings and saves are
untouched. A partial removal retains the ownership record for repair.

FixWorld's runtime always starts through Doorstop. Without it, the ordinary Mod
constructor performs only installation and restart. There is no separate normal
runtime or persistent installation opt-out. After uninstall, remove or disable
FixWorld before launching again; otherwise the same first-install flow runs again.
Disabling FixWorld still prevents its normal mod construction and early services.

### Early startup

The preloader executes inside RimWorld, not in a separate process:

```text
Doorstop
  -> active ModsConfig check
  -> wait for Assembly-CSharp and loaded Harmony 2
  -> load adjacent canonical FixWorld.dll
  -> StartEarly: one engine-independent service graph

RimWorld creates FixWorldMod normally
  -> attach the same assembly/controller to its ModContentPack
  -> initialize mod-dependent services and queue Unity proxy creation
  -> static late initialization schedules remaining library initialization
  -> Ready only after that work succeeds
```

The preloader observes the Harmony assembly actually loaded by the game. It does
not scan Workshop folders, pick a DLL heuristically or enforce a fixed game MVID.
Early entry timing therefore depends on when that dependency becomes available.

The activation check reads the effective save directory, including
`-savedatafolder=...` and the separate argument form. Missing config or inactive
FixWorld prevents early core loading. Malformed/unreadable config fails closed
with a bootstrap log. There are no DDS jobs, Unity calls or Harmony patches on
the disabled entry path.

Lifecycle phases distinguish Cold, Waiting, Starting, CoreReady, Attaching,
Attached, Completing, Ready, RestartPending, Disabled, Failed and Stopped.
Repeated calls reuse the same identity and services. Different assembly or
content ownership is rejected. Failure is not published as completion.
Per-frame/tick readiness reads do not take the lifecycle mutation lock.

The normal Mod constructor is not suppressed or manually instantiated.
RimWorld still registers the assembly and supplies its ModContentPack.
Unity setup remains at the existing main-thread/long-event boundary.
The profiler/store do not decide whether bootstrap succeeded. The typed
`fixworld.bootstrap` telemetry record presents phase and failure independently.

## Restart protocol

1. Parent creates a unique ready/commit/cancel handshake and starts a hidden helper.
2. Helper validates target, working directory, arguments and parent PID/start time.
3. Helper signals ready. Only after acknowledgement does the parent commit and
   call the game's shutdown function.
4. Helper waits for the identified parent to exit. Cancellation or a missing
   commit prevents launch. It never kills the game.
5. Helper starts one replacement with preserved arguments/working directory,
   without inherited DOORSTOP_INITIALIZED or old FixWorld readiness markers.

Readiness has a bounded timeout. A hung shutdown leaves the helper waiting rather
than creating a second game. Shutdown exceptions cancel the helper. A failed
replacement launch is recorded in FixWorld.Restart.log beside the helper.
The game-root FixWorld.Bootstrap.log covers early-entry failures.
Normal runtime messages use the fork's logger.

## Package layout

Build output belongs under `mod/FixWorld/Mods/FixWorld`:

```text
v1.6/Assemblies/FixWorld.dll
v1.6/Assemblies/FixWorld.Bootstrap.dll
Tools/FixWorld.Restart.exe
Tools/FixWorld.Restart.exe.config
Tools/Doorstop-4.4.0/winhttp.dll
Tools/Doorstop-4.4.0/UnityDoorstop-LICENSE.txt
Tools/Doorstop-4.4.0/manifest.json
```

The main project builds Bootstrap and RestartHelper project references. There is
no copy of FixWorld.dll in Tools and no dependency on archived source. Native
Doorstop remains an unmodified separately licensed binary.

**Legacy installs are not adopted automatically.** Close RimWorld and use the old
installation's removal procedure before testing this package. Do not blindly
delete winhttp.dll or another loader's configuration. The new installer reports
unknown/legacy files as a conflict.

## Verification

Build the fork with the local-reference command described in
[telemetry](telemetry.md#verification), redirecting output into
`temp/fork-validation` to avoid game deployment.

Run:

```powershell
dotnet run --project mod/FixWorld/Tests/Bootstrap.Contracts/FixWorld.Bootstrap.Contracts.csproj -c Release -- <FixWorld.Restart.exe> <FixWorld.dll> <0Harmony.dll> <RimWorld-Managed-directory>
```

The helper path alone runs the engine-independent suite. The four arguments add
isolated processes that invoke the actual managed Doorstop entry against real
managed game references: enabled entry, dependency wait, repeated core identity
and disabled entry. No native game process is started.

Tests cover lifecycle failure/duplication/concurrency, config parsing, owned
installation/repair/conflict/pending states, quoting, helper readiness failure,
shutdown cancellation, process-exit ordering and preserved launch state.
Installation fixtures use synthetic binaries in temporary directories, never
the real game root.

The fixtures cover installation after removal, foreign residual files and disabled
activation. Actual-assembly fixtures check core identity but do not invoke
native menu initialization. Desktop CLR cannot fully patch the game's Mono/Unity
methods (`Dictionary.TryAdd` and native patch support differ); these observer
failures are recorded in `FixWorld.Bootstrap.log` without failing core startup.
Passing these fixtures does not establish that early hooks work in-game.

Still required in-game: clean first install -> one restart -> ready library;
normal boot -> same controller attachment; Mods-tab restart; disabled FixWorld
-> no core initialization. Desktop CLR tests do not prove native Doorstop
injection, Unity Mono loading or the engine's shutdown behavior.

### First native run, 2026-09-05

The hash-verified legacy proxy/config/manifest and original junction were moved
to `RimWorld/FixWorld.previous-20260905-210119` (recoverable, not deleted).
The mod junction now targets `mod/FixWorld/Mods/FixWorld`.

Process 49016 installed Doorstop, exited, and the helper launched process 9512.
The new manifest cleared RestartPending; typed bootstrap telemetry reached Ready.
The collector retained 54 complete samples with no validation warnings or caught
library callback errors. Evidence: `temp/startup-20260905-210119/capture` (local,
ignored). The actual main menu was visible; the game was left open.

The run exposed a fork-specific version-check defect: Allow Tool's HugsLib
`requiredLibraryVersion` was being compared to FixWorld 0.1.0. FixWorld now reads
only `requiredFixWorldVersion` from About/Version.xml. HugsLib's original tag is
not interpreted as a FixWorld dependency. Regression fixtures test both tags
together and preserve overrideVersion metadata. The corrected build passed local
validation but was not loaded into this already running process.

Native Mods-tab restart, disabled entry and the corrected version dialog remain
unverified. The loading UI has not yet been restored from the archive.
