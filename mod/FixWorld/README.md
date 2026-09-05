# FixWorld

Parent: [Project README](../../README.md)

Fresh RimWorld 1.6 rewrite based directly on HugsLib source. Projects, namespaces,
mod metadata and translation keys use FixWorld. Only English and German remain.

The previous FixWorld implementation is archived locally. DDS, telemetry,
caching and installation functionality have not yet been brought into this base.
No compilation or runtime compatibility is claimed at this stage.

The retained 1.6 prebuilt DLL has only been renamed on disk. Its internal assembly
identity and implementation are still upstream HugsLib until the first rebuild.
Do not distribute it as a compiled FixWorld release.

FixWorld changes use MPL-2.0; original HugsLib material remains public domain.
See `license.txt` and `NOTICE.txt` for licensing and attribution.
