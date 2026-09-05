# FixWorld

Parent: [Project README](../../README.md)

Fresh RimWorld 1.6 rewrite based directly on HugsLib source. Projects, namespaces,
mod metadata and translation keys use FixWorld. Only English and German remain.

The previous implementation is archived locally. Typed telemetry/profiling,
caching, News image ownership and bootstrap installation now live in this fork.
DDS and the old loading/diagnostics UI are separate restoration slices.

`FixWorld.csproj` builds the fork, bootstrap and restart helper. The Python build
script stages binaries in `temp/build`; `Mods/FixWorld` contains the deployable
content. The package overlays only our current binaries, never game references
or stale assemblies from the content directory. See the
[development guide](../../docs/development.md) for build and test commands.

FixWorld changes use MPL-2.0; original HugsLib material remains public domain.
See `license.txt` and `NOTICE.txt` for licensing and attribution.
