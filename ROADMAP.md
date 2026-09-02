# FixWorld roadmap

Parent: [Project README](README.md)

FixWorld aims to reduce RimWorld startup and simulation cost without changing
gameplay behavior, mod order, or save data. Measurements decide the order of
work. The [TODO](TODO.md) owns concrete tasks.

## 1. Stabilize the owned loader

- Reduce the current DDS subsystem without changing its cache identity or output.
- Publish one cheap runtime diagnostics snapshot for logs, benchmarks, and UI.
- Attribute deferred main-thread work to producers and mods before optimizing it.
- Keep one deterministic play-data path with explicit RimWorld fallbacks.

## 2. Own expensive loading stages

- Split assembly discovery, assembly loading, mod construction, and Harmony work.
- Measure XML reading, patching, definition import, reference resolution, and
  finalization independently.
- Replace one measured RimWorld-owned operation at a time while preserving
  produced data and ordering.

## 3. Prepare work off the main thread

- Move only independent file, hash, cache, and byte preparation to workers.
- Commit immutable results in original order on the main thread.
- Set CPU, I/O, and memory budgets from measurements on NVMe, SATA SSD, and HDD.
- Evaluate Unity Jobs only through an isolated prototype against RimWorld 1.6.

## 4. Runtime performance

- Capture a repeatable late-game save baseline.
- Separate tick, frame, Unity Job, FixWorld worker, and main-thread time.
- Start with the dominant measured hotpath.
- Investigate pathfinding requests, invalidation, reachability, and safe path
  reuse only after instrumentation exists.

## Compatibility boundary

FixWorld currently supports one exact RimWorld build on Windows x64. Broader
version compatibility, Linux conversion support, new texture formats, DDS packs,
and alternative detour backends remain future work until the current pipeline is
stable and measured.
