# Third-party notices

Parent: [Project README](README.md)

The current fork redistributes the unmodified UnityDoorstop Windows x64 binary.
It is not covered by FixWorld's MPL-2.0 license. The original DDS tooling remains
archived and is not part of the current package.

## UnityDoorstop 4.4.0

- Project: [NeighTools/UnityDoorstop](https://github.com/NeighTools/UnityDoorstop)
- Release and source tag: [v4.4.0](https://github.com/NeighTools/UnityDoorstop/releases/tag/v4.4.0)
- Bundled file: `mod/FixWorld/Mods/FixWorld/Tools/Doorstop-4.4.0/winhttp.dll`
- License: GNU Lesser General Public License 2.1
- Local license copy:
  `mod/FixWorld/Mods/FixWorld/Tools/Doorstop-4.4.0/UnityDoorstop-LICENSE.txt`

The bundled manifest records the upstream artifact URL and expected hashes.
FixWorld loads UnityDoorstop as a separate proxy DLL and does not incorporate its
source into FixWorld assemblies.

## DirectXTex texconv (archived, not bundled by this fork)

- Project: [Microsoft DirectXTex](https://github.com/microsoft/DirectXTex)
- Bundled file: `mod/FixWorld/Tools/Windows-x64/texconv.exe`
- Recorded file version: `2026.5.8.1`
- License: MIT
- Local license copy: `mod/FixWorld/Tools/DirectXTex-LICENSE.txt`

FixWorld invokes texconv as an external process through a typed command-line
wrapper. DirectXTex source is not included in this repository.

## Harmony and RimWorld

Harmony is a required RimWorld mod but is not bundled. FixWorld resolves the
installed Harmony assembly at runtime.

RimWorld assemblies and decompiled RimWorld source are not distributed. Local
reference policy and provenance are documented under [decompiled](decompiled/README.md).
