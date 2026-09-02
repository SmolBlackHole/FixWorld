# Develop and verify FixWorld

Parent: [Documentation index](README.md)

FixWorld targets .NET Framework 4.7.2 because that is the compatible runtime
surface used by RimWorld. The repository uses .NET SDK 10 to build the projects.
The complete build is Windows-only and requires local RimWorld and Harmony
assemblies that are never redistributed by this repository

## Prerequisites

- Windows x64
- RimWorld `1.6.4871 rev591`
- Harmony installed as a RimWorld mod
- .NET SDK 10
- Python 3.11 or newer
- Git

## Configure local references

Copy the example file:

```powershell
Copy-Item .\mod\FixWorld\Local.Build.props.example `
    .\mod\FixWorld\Local.Build.props
```

Edit the copy so `RimWorldRoot` points to the local game directory and
`HarmonyAssemblyPath` points to the installed `0Harmony.dll`. The file is ignored
by Git

The same values can be supplied without a file:

```powershell
$env:RIMWORLD_ROOT = '<RimWorldRoot>'
$env:RIMWORLD_HARMONY_ASSEMBLY = '<HarmonyModRoot>\Current\Assemblies\0Harmony.dll'
```

## Run repository checks

```powershell
python .\tools\check.py
```

The check validates UTF-8 and LF text, the public Markdown link graph, tracked
artifact policy, Python syntax, and the Shared contract suite. It intentionally
does not pretend to compile RimWorld-dependent projects without proprietary game
assemblies

## Build and package

```powershell
python .\tools\build.py
python .\tools\build.py --package
```

The normal build writes the mod assembly under `mod/FixWorld/Assemblies` and
runtime components under `mod/FixWorld/Tools/Windows-x64`. Generated FixWorld
assemblies and packages are ignored. The package command writes
`dist/FixWorld-pilot-win-x64.zip`

## Benchmark

Set `RIMWORLD_ROOT` or pass `--game-root` explicitly:

```powershell
python .\tools\benchmark.py `
    --game-root '<RimWorldRoot>' `
    --live-mods `
    --variant 'descriptive-variant-name'
```

The Runtime writes its versioned telemetry snapshot as typed benchmark JSON.
Python starts RimWorld, waits for the report, validates its schema, writes one
`loader-stages.csv`, and appends one result row. Do not compare
runs with different mod lists, cache states, or source fixtures as if they were
the same experiment

Raw profiles, saves, logs, generated reports, and decompiled game code remain
local. The tracked benchmark CSV contains only deliberately selected aggregate
results

## Public release checklist

Before changing repository visibility or publishing an archive:

1. Run `python tools/check.py`
2. Run `python tools/build.py --package` with the supported RimWorld build
3. Complete at least one full-mod-list launch to the main menu
4. Verify preloader status, uninstall, reinstall, and restart-loop prevention
5. Verify the DDS cache on a rebuild and a warm start
6. Inspect the archive for local configuration, saves, logs, and generated data
7. Run a dedicated secret scan and review the Git history, not only the current tree
8. Confirm all bundled binary versions and licenses in
   [third-party notices](../THIRD_PARTY_NOTICES.md)

CI covers only checks that can run without a RimWorld installation. A green CI
run is not evidence that the complete mod loads in the game
