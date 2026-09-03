# DDS texture cache

Parent: [Documentation index](README.md)

FixWorld caches eligible mod textures as BC7 DDS so later launches can skip
repeated PNG or JPEG decoding, mipmap generation, and runtime compression. A
cache failure must never change game content or prevent the original texture
from loading.

## Measured pilot baseline

The current 88-mod reference list contains 10,460 reusable DDS entries in 62
per-mod packs. Recent fully warm runs spend about 0.3 seconds in texture loading
and about 0.1 seconds reading packed DDS data. Earlier measurements of the
loose-file implementation reduced a roughly 21.16-second source-texture path to
2.24 through 2.51 seconds with a complete cache. The current packed cache is
about 1.6 GiB.

These numbers describe one machine, one mod list, and one cache identity. They
are not a general performance guarantee. Raw runs and comparison rules belong
to the benchmark data, not this document.

Rebuilding 8,250 missing entries into 52 packs took 282 seconds in the
background with one active converter. Reading 256 MiB of packed data ahead of
the Runtime was neutral for total startup time on the reference NVMe. Slow
storage requires separate measurements.

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
and conversion work, but publication remains ordered and atomic. Warm access
timestamps are updated per pack at most every 12 hours, avoiding a complete
manifest rewrite on every launch.

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

## BC7 quality checks

An automated comparison decoded 10,344 packed BC7 top mip levels and compared
them with their PNG or JPEG sources. Mean luminance ratios remained close to
1.000 with and without PNG gamma, sRGB, or ICC metadata. The reported darker
runtime appearance therefore still requires a named affected texture and an
in-game check of Unity sampling, alpha handling, and generated mip levels.

A 40-texture converter sample took 956 ms with normal GPU BC7, 674 ms with quick
GPU BC7, and 21.9 seconds on the CPU. Quick mode increased mean top-level RGBA
error from 0.291 to 0.456 values out of 255. Normal GPU quality remains the
default until the runtime appearance issue is resolved.

## Measurement rules

Cold application cache, warm application cache, and warm operating-system file
cache are different states. A cache build warms the OS cache and therefore does
not count as an independent cold follow-up run. A/B comparisons must use the
same RimWorld build, mod list, source fixture, cache identity, and worker policy
