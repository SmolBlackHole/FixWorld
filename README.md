# FixWorld

FixWorld is an experimental performance mod for RimWorld 1.6 on Windows x64.
It enters the process through UnityDoorstop, validates the exact RimWorld build,
and starts a small runtime before RimWorld loads active mods.

The current pilot focuses on two measured concerns:

- passive stage diagnostics for RimWorld's original play-data loader;
- a persistent DDS texture cache that avoids repeated image decoding, mipmap
  generation, and BC3 compression on later launches.

FixWorld is not a stable release yet. It currently targets RimWorld
`1.6.4871 rev591`, requires Harmony, and deliberately disables its early runtime
when its version contract cannot be proven.

## Important behavior

The first enabled launch installs the bundled UnityDoorstop proxy next to
`RimWorldWin64.exe` and restarts RimWorld once. FixWorld does not rewrite the
original RimWorld assemblies, but it does add files to the game directory.

The installer refuses to overwrite an unknown `winhttp.dll`. The bundled tool
can inspect, repair, or remove only files that FixWorld can prove it owns. Read
[Windows loader installation and recovery](docs/windows-preloader.md) before
testing the mod.

## Install a pilot build

1. Close RimWorld
2. Install and enable the RimWorld Harmony mod
3. Extract the packaged `FixWorld` directory into RimWorld's `Mods` directory
4. Enable FixWorld after Harmony in the mod list
5. Start RimWorld and allow the one-time automatic restart

If the restart or early loader fails, close RimWorld and use the recovery and
uninstall commands in the [Windows loader guide](docs/windows-preloader.md).

## Build from source

Requirements:

- Windows x64;
- .NET SDK 10;
- Python 3.11 or newer

Run:

```powershell
python .\tools\check.py
python .\tools\build.py
```

Create a distributable pilot archive with:

```powershell
python .\tools\build.py --package
```

The portable build uses pinned, compile-only RimWorld `1.6.4871` and Harmony
reference packages. For local development against the exact installed binaries,
pass `--game-root <RimWorldRoot>` and `--harmony <0Harmony.dll>` to the build
script. Builds go to `temp/build`, not the live game. Local
game assemblies are never committed or redistributed by this repository

## Documentation

Start at the [documentation index](docs/README.md):

The [fork source overview](mod/FixWorld/README.md) describes the package layout.

- [Architecture](docs/architecture.md) explains the preloader, loader, runtime,
  mod bridge, ownership, and failure boundaries
- [Play-data pipeline](docs/play-data-pipeline.md) documents the 17 loading
  stages observed around RimWorld's original loader
- [Runtime diagnostics](docs/diagnostics.md) documents telemetry, benchmark
  output, and the in-game diagnostics window
- [Development and verification](docs/development.md) owns local setup, builds,
  checks, benchmarks, and release preparation
- [DDS texture cache](docs/dds-cache.md) documents cache behavior and limits.
- [Roadmap](ROADMAP.md) contains only future direction
- [TODO](TODO.md) tracks concrete open engineering work

## Project status

FixWorld is under an explicit feature freeze while the existing DDS subsystem
and runtime diagnostics are reduced and measured. Public
issues and pull requests should not assume compatibility beyond the supported
RimWorld build

## License and third-party software

FixWorld source code is licensed under the [Mozilla Public License 2.0](LICENSE).
Bundled UnityDoorstop and DirectXTex binaries retain their own licenses. See
[Third-party notices](THIRD_PARTY_NOTICES.md)

RimWorld is developed by Ludeon Studios. FixWorld is an independent project and
is not affiliated with or endorsed by Ludeon Studios
