# Optimization method

Parent: [Documentation index](README.md)

FixWorld optimizes measured RimWorld operations without replacing their
observable semantics. RimWorld remains responsible for mod order, XML patch
order, Def publication, recovery, and deferred work. FixWorld may cache inputs,
remove repeated discovery, or replace one measured method when the replacement
has a smaller compatibility surface than the original operation.

## Selection rules

1. Measure the unmodified method on the current game build and representative
   mod lists.
2. Separate cold-cache, warm-cache, CPU, storage, worker, main-thread, and GPU
   time before choosing an implementation.
3. Patch the smallest method that owns the repeated work. Do not take ownership
   of the surrounding pipeline merely to reach it.
4. Preserve effective mod order and all documented side effects unless a
   deliberate compatibility break is separately approved.
5. Keep Unity object creation, GPU upload, and Verse global-state publication on
   their required thread. Workers may prepare immutable data and file payloads.
6. Reuse one canonical discovery result downstream. A cache hit must avoid the
   original I/O or computation, not repeat it before returning cached data.
7. Validate persistent artifacts from the inputs that determine their result,
   including game version, schema, converter identity, mod identity, source
   length, and source modification time. Publish updates atomically.
8. Compare the change and its control with the same build, mod sequence, cache
   state, settings, and monitor. Reject changes that only move time between
   stages or lack a material end-to-end gain.

## Current evidence

The direct-loader prototype did not improve the 89-mod warm path materially.
The 260-mod fixture forced RimWorld recovery from 259 expected mods to 5 and
produced 4,893 relevant errors. Assembly scheduling and complete loader
ownership therefore remain isolated on `experiments/direct-loader` and are not
part of the supported runtime.

The DDS cache remains useful because it replaces repeated source decoding with
validated block-compressed payloads. Its startup reconciliation now scans the
cache index once for all active packages. On a cold cache, source dimension and
exact BC7 size discovery run in the background after the main menu. A 90-mod
validation built 10,468 entries with no failures.

Texture discovery is owned by RimWorld at its original deferred content-load
boundary. FixWorld observes the returned dictionary and uses the same instance
to prepare DDS hits and background build plans before RimWorld enumerates it.
This preserves files generated during mod construction and removes FixWorld's
earlier scan. The measured 90-mod fixture previously made 180 equivalent calls,
including 90 repeated scans costing 68.2 ms. After the change, a warm run kept
10,468 DDS hits, reported no relevant errors, and spent 0 ms in FixWorld's
`IndexTextureSources` stage.

`GlobalTextureAtlasManager.BakeStaticAtlases()` is not a current target. Two
warm-DDS measurements took 653 ms and 674 ms. GPU color blitting used 438-460
ms, compression 91 ms, layout 60-63 ms, and mesh creation 23-29 ms. Replacing
the atlas builder would create more compatibility and rendering risk than the
measured startup budget justifies.

## Next target

Profile the next largest individual RimWorld method under the unmodified loader.
Patch it only when the measured saving materially exceeds the compatibility
surface. Do not infer a target from the duration of its surrounding stage.
