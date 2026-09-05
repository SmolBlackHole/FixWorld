# DDS texture cache

Parent: [Documentation index](README.md)

FixWorld caches eligible PNG/JPEG mod textures as BC7 DDS packs. A later launch
loads their compressed mip data directly into Unity, avoiding image decoding,
mipmap generation and runtime compression. RimWorld still owns discovery,
content order and the resulting textures. Shipped DDS files keep RimWorld's
native loader; failed or unsupported cache entries use the source texture.

## Components

- `TextureHooks` installs one early Harmony adapter for content discovery and
  texture loading. Normal mod attachment does not install a second copy.
- `TextureDdsCache` owns discovery plans, the process-lifetime serial background
  task and maintenance.
- The shared `CacheStore` caches mapped pack readers on Unity's main thread.
  The DDS service owns their disposal, releases all mappings before background
  publication and does not own/destroy textures already given to RimWorld.
- `DdsPackStore` owns the persistent disk index and immutable discovery snapshots.
  This is file-format state, not a second general-purpose cache framework.
- `fixworld.dds` publishes typed snapshots through the shared telemetry store.
  The diagnostics view reads them directly; the presenter supplies logs and JSONL.
  Cached profiler slots measure discovery, uploads and completed build durations.

Only the main thread uses Unity. The worker handles files and the external
converter, survives colony/menu transitions, and stops on runtime shutdown.
Cancellation terminates texconv; final store disposal belongs to the worker if
it is still unwinding. No timeout disposes resources underneath an active job.

## Validity and publication

`index.json` records package/source path, source length and modification time,
source hash, converter hash, artifact slice and last use. Startup freshness uses
size/time plus converter identity, not a full source hash on every launch.
Changed entries are converted again. The existing
`bc7-unorm-gpu-mips-v1-mod-pack` identity and schema 2 remain readable.

Writes use a temporary file, flush and atomic replacement with
`index.backup.json`. Each rebuilt pack gets a unique artifact name, including
repairs with unchanged source stamps. A corrupt cached DDS falls back immediately
and becomes a background rebuild candidate. Slice bounds, BC7/DX10 2D format,
dimensions, mip count and exact payload length are checked before Unity upload.

Only eligible dimensions are converted. Unsupported images and non-divisible
dimensions remain on RimWorld's path. Disabling RimWorld texture compression or
lacking BC7 support also leaves source texture loading to RimWorld.

## Background builds and controls

Missing entries are prepared after the runtime reaches Ready. One below-normal
worker invokes one converter at a time, using the established BC7_UNORM,
ignore-sRGB, vertical-flip and mip-count settings. Existing cache hits remain
usable if texconv is unavailable. Budget maintenance also runs on warm starts
with no conversions. There is no automatic migration/deletion of the old loose
`dds-v1` cache and no archived FixWorld.Tool dependency.

Open **Mod Options -> FixWorld -> DDS cache**, or the in-game FixWorld main-bar entry:

- **Clear DDS cache** asks for confirmation, queues removal of generated packs, and leaves
  source textures and already loaded game textures alone. Restart to rebuild.
- **Retry DDS builds** restarts failed mod builds. Both controls are unavailable
  during startup or while the worker is active.

The page embeds persistent settings with validation, info icons and reset:

- Cache limit: **6 GiB** by default. Zero removes the packs and prevents new writes.
- Free-drive reserve: **10 GiB** by default. This takes priority over the limit,
  even if the cache must shrink below it or be emptied.

Maintenance runs on the serial worker after startup, on settings changes and
every 30 seconds. It reports a warning when deleting the cache cannot free enough
space. Drive measurements and eviction never run while drawing the UI. An active
conversion finishes before changed limits are enforced. Other applications can
consume disk space between checks, so this is not a filesystem quota.

Environment overrides take precedence over saved values and are marked in the UI:

| Variable | Meaning |
| --- | --- |
| `FIXWORLD_DDS_CACHE=0` | Disable the DDS cache |
| `FIXWORLD_DDS_CACHE_ROOT` | Dedicated cache directory |
| `FIXWORLD_DDS_CACHE_MAX_GIB` | Non-negative cache budget |
| `FIXWORLD_DDS_CACHE_MIN_FREE_GIB` | Non-negative free-space reserve |
| `FIXWORLD_TEXCONV_PATH` | Explicit converter executable |

Use a dedicated directory for a root override, never one containing unrelated files.

The Windows package contains the pinned DirectXTex texconv executable and MIT
license. SHA-256: `DCFDEC10244E02CF5037FBA089C55FB7E1326B1C8181742D77D15FA5CB5EEF06`.
See [third-party notices](../THIRD_PARTY_NOTICES.md). Linux creation is unverified.

## Verification

98 net472 contracts cover disk-index roundtrips, freshness, writer exclusion,
backup recovery, malformed slices, publication, eviction/clear, exact BC7
payload sizes, converter arguments, Unicode paths, Windows quoting and actual
child-process cancellation, zero budgets, reserve priority and actual settings
validation/reset/XML persistence. These tests use a converter fixture, not Unity.

Native cache run before the settings UI change: `temp/dds-fork-native/Player.log`,
telemetry session `d681f13b7578488ea9f49b1bf36a2a0f`:

- 10,471 successful DDS uploads from 63 mapped packs, zero DDS failures.
- 167 initial misses: 162 excluded dimensions, 5 newly built entries.
- Upload scopes total 1,731.11 ms; reader creation 152.44 ms; discovery 397.87 ms.
- Background builds total 1,370 ms. Cache size 1,700,168,976 bytes.
- Ready/main menu reached. Shared callback error count remained zero.

This proves the restored cache-hit and small background-build paths, not a
controlled total-startup speedup or a complete visual audit. Native cache-button
actions and colony/menu transitions during a long build remain separate checks.
The existing HugsLib ModOptions patch warning appears in both the preceding UI
run and this DDS run; it is not a newly introduced DDS error.

## Historical results, not fork benchmarks

The archived pilot reported about 21.16 seconds for source textures versus
2.24-2.51 seconds with a complete loose-file cache. An 88-mod pack experiment
reported 10,460 entries in 62 packs, about 1.6 GiB. Rebuilding 8,250 entries took
282 seconds with one active converter. These measurements use different scopes
and runs and cannot be compared directly with the fork upload scope above.

Archived image comparisons found mean top-level luminance near 1.000 across
10,344 BC7 textures. A reported darker in-game texture still needs a named
example and Unity sampling/alpha/mip inspection. GPU quality remains unchanged.

Cold application cache, warm application cache and warm OS file cache are
different states. Cache creation warms the OS cache; a subsequent launch is
not an independent cold run.
