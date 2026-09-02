# DDS texture cache

Parent: [Documentation index](README.md)

FixWorld caches eligible mod textures as DDS so later launches can skip repeated
PNG or JPEG decoding, mipmap generation, and BC3 compression. A cache failure
must never change game content or prevent the original texture from loading.

## Measured pilot baseline

The current 88-mod reference list contains 10,460 reusable DDS entries. Earlier
measured runs reduced the texture path from about 21.16 seconds without DDS to
2.24 through 2.51 seconds with a complete warm cache. The cache occupied about
1.59 GiB

These numbers describe one machine, one mod list, and one cache identity. They
are not a general performance guarantee. Raw runs and comparison rules belong
to the benchmark data, not this document

## Validity

`index.json` records the source path, source size and modification time, content
hash, converter identity, output artifact, and last use. Size and modification
time provide the fast startup path. A changed source is hashed before FixWorld
decides whether conversion is actually required

Index publication uses a temporary file, flush, and atomic replacement. The
previous index remains available as `index.backup.json`. Missing, corrupt, or
incompatible entries are cache misses and fall back to the original texture

The cache identity includes every option that can affect pixels. The current
identity also distinguishes sRGB handling so older DDS files that could render
too dark are rebuilt rather than reused

## Build and maintenance

Cache validation occurs during loading. Missing DDS files are built later as
low-priority background jobs after the menu is usable. Workers may perform file
and conversion work, but publication remains ordered and atomic

The default disk limit is 6 GiB and can be changed between 1 and 64 GiB in the
FixWorld settings. FixWorld also keeps at least 10 GiB of free disk space.
`FIXWORLD_DDS_CACHE_MAX_GIB` provides an explicit test override

When space is insufficient or `texconv` is unavailable, FixWorld loads the
original texture. Removed sources and disabled mods become cleanup candidates.
Least-recently-used entries are evicted when the configured limit is exceeded

After an upgrade from the loose-file pilot cache, FixWorld removes the owned
`dds-v1` directory automatically. The migration waits for the new pack builds
and runs as a low-priority background I/O job, so deleting thousands of old
files does not extend the startup path. It is idempotent and is skipped when a
custom active cache root cannot be mapped safely to the standard legacy sibling

Inspect or remove that legacy cache manually without Python:

```powershell
.\Tools\Windows-x64\FixWorld.Tool.exe dds-cache status
.\Tools\Windows-x64\FixWorld.Tool.exe dds-cache clean
```

`status` is read-only. `clean` removes the complete FixWorld-owned legacy
`dds-v1` directory and refuses to run while RimWorld is active

## Converter boundary

Windows builds bundle `texconv.exe` from DirectXTex. Only the typed tool wrapper
knows its command-line arguments. Runtime code passes paths and conversion
requirements rather than starting the process directly

The bundled DirectXTex build and license are documented in
[third-party notices](../THIRD_PARTY_NOTICES.md). Linux conversion is not yet
implemented. Existing DDS files may be platform-neutral, but cache creation
still requires an explicitly supported converter backend

## Measurement rules

Cold application cache, warm application cache, and warm operating-system file
cache are different states. A cache build warms the OS cache and therefore does
not count as an independent cold follow-up run. A/B comparisons must use the
same RimWorld build, mod list, source fixture, cache identity, and worker policy
