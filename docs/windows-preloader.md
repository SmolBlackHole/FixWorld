# Windows preloader

Parent: [Documentation index](README.md)

FixWorld uses [UnityDoorstop 4.4.0](https://github.com/NeighTools/UnityDoorstop/releases/tag/v4.4.0)
for its early Windows x64 process entry.

## First boot

The normal RimWorld mod owns installation policy. On the first launch it:

1. validates the bundled Doorstop and FixWorld preloader;
2. refuses to overwrite an unknown `winhttp.dll` or Doorstop configuration;
3. removes the superseded `FixWorld.dll` deployment if present;
4. installs the managed files next to `RimWorldWin64.exe`;
5. records a pending restart atomically; and
6. restarts RimWorld once

The managed files are:

- `winhttp.dll`, the verified UnityDoorstop 4.4.0 binary;
- `doorstop_config.ini`, marked as FixWorld-owned; and
- `FixWorld.preloader.json`, the versioned installation manifest.

The manifest records its schema, Doorstop version and hash, configuration hash,
preloader path and hash, and whether the first restart is still pending. This lets
FixWorld distinguish an owned outdated installation from another proxy DLL and
repair a moved or updated mod without overwriting foreign files

If the first restart does not activate the preloader, FixWorld does not restart
again. The normal RimWorld loader continues and FixWorld stays disabled for that
launch

## Normal boot

```text
RimWorldWin64.exe
  -> UnityDoorstop
  -> FixWorld.Preloader
     -> wait for Assembly-CSharp
     -> resolve the installed Harmony 2 assembly
  -> FixWorld.Loader
     -> validate the RimWorld MVID and runtime contract
     -> load FixWorld.Runtime
  -> FixWorld.Runtime.StartEarly()
     -> install the safe LoadAllPlayData bootstrap hook
     -> activate runtime hooks at the play-data boundary
     -> observe RimWorld's original loader and route DDS cache hits
  -> CreateModClasses
     -> FixWorld.Mod attaches settings and its ModContentPack
```

The delayed hook activation is intentional. Installing runtime hooks at the
Doorstop entry caused Unity resource initialization while the engine was not ready.
The bootstrap hook enters early but installs stage and texture hooks only at
`PlayDataLoader.LoadAllPlayData()`

The normal `FixWorld.Mod.dll` is not a second runtime. It remains the RimWorld-facing
installer, settings UI, and `ModContentPack` adapter for the already running
`FixWorld.Runtime`

After the runtime hooks are active, the mod confirms the installation and clears
`restartPending`. If Doorstop is active but the runtime did not activate, FixWorld
disables itself for that launch and leaves the original RimWorld loader intact

## DDS read-ahead

DDS read-ahead is optional best-effort preloader work. Its failure cannot disable
the loader bridge. The default budget is the smaller of 256 MiB and one eighth of
available physical memory. `FIXWORLD_DDS_READ_AHEAD_MIB=0` disables read-ahead only

## Status, repair, and removal

Close RimWorld before manually changing the installation:

```powershell
.\Tools\Windows-x64\FixWorld.Tool.exe preloader status
.\Tools\Windows-x64\FixWorld.Tool.exe preloader install
.\Tools\Windows-x64\FixWorld.Tool.exe preloader uninstall
```

The tool and normal mod use the same installation implementation. Uninstall removes
only files proven to be FixWorld-owned. Linux remains a separate future port
