# Develop and verify FixWorld

Parent: [Documentation index](README.md)

## Prerequisites

Windows x64, .NET SDK 10, Python 3.11+ and Git. The fork targets .NET Framework
4.7.2. Portable builds use compile-only RimWorld `1.6.4871` and Harmony `2.4.1`
packages. No game assemblies are shipped in the ZIP.

## Checks, builds and packages

```powershell
python tools/check.py
python tools/build.py --package
```

Checks cover repository hygiene, reachable Markdown documentation, Python
syntax and unit tests, and Telemetry, Caching, News and Bootstrap contracts.
Bootstrap tests start fixture processes, not RimWorld. Full native bootstrap
acceptance remains separate. Windows checkout CRLF is accepted because Git
normalizes tracked text to LF; ignored archives and local game data are not scanned.

Builds stage all three binaries in `temp/build`. They do not deploy, start or
stop RimWorld. `--package` writes `dist/FixWorld-pilot-win-x64.zip`, with a single
`FixWorld/` mod directory. Its content is selected explicitly: mod metadata,
definitions, translations, textures, Doorstop, licenses and our built binaries.
Old DLLs, PDBs, sources, caches and logs in the content directory are not packaged.

Build against installed references with:

```powershell
python tools/build.py --package --game-root '<RimWorldRoot>' --harmony '<HarmonyModRoot>/Current/Assemblies/0Harmony.dll'
```

Alternatively set `RIMWORLD_ROOT` and `RIMWORLD_HARMONY_ASSEMBLY`. Explicit invalid
paths fail instead of silently using another reference version. Omit local paths
for the same portable package build used in CI.

After a local-reference build, the additional actual-assembly bootstrap fixture is:

```powershell
dotnet run --project mod/FixWorld/Tests/Bootstrap.Contracts/FixWorld.Bootstrap.Contracts.csproj -c Release -- temp/build/FixWorld.Restart.exe temp/build/FixWorld.dll '<HarmonyDLL>' '<RimWorldRoot>/RimWorldWin64_Data/Managed'
```

## Deploy and capture

Close the game before replacing its mod binaries. Extract the ZIP into the game's
Mods directory, or overlay it onto `mod/FixWorld/Mods/FixWorld` when that directory
is the target of the local Mods/FixWorld junction. Do not replace the junction itself.

```powershell
python tools/rimworld_process.py --game-root '<RimWorldRoot>' --monitor 1
python tools/telemetry.py collect --seconds 60 --game-root '<RimWorldRoot>'
```

The launcher refuses a duplicate running process. It positions the initial game
window and leaves the game open; a bootstrap restart may replace that process.
The collector discovers subsequent telemetry sessions. See
[capture semantics and analysis](harness.md). There is no legacy schema-19 runner.

## Release boundary

CI runs the same checks and portable package build. A successful main push
publishes a pilot prerelease. Compile/test/package success is not an in-game
compatibility claim. Before promotion, test startup, disable/re-enable, restart
and the changed UI or gameplay behavior, then inspect the package and notices.
See [bootstrap acceptance and recovery](windows-preloader.md).
