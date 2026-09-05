# Typed caches

Parent: [Runtime modules](runtime-modules.md)

`FixWorldController.Caches` owns the shared registry. `ModBase.Caches` exposes
that same registry. `CacheContract<TKey, TValue>` defines identity, capacity,
factory and key equality. Consumers create a cache once and retain the
`TypedCache<TKey, TValue>` handle. Lookup does not resolve a registry name.

## Behavior and lifetime

- Storage is a typed dictionary with bounded FIFO eviction. Hits neither move a
  list node nor allocate, clone a dictionary, format text or call the profiler.
  Misses create one value; successful insertion evicts the oldest entry if full.
- Null/default values are valid hits. Exceptions are not cached. Factory timing
  and failures use the shared profiler; business hits/misses do not depend on it.
- `Invalidate(key)` removes exactly one entry. `Clear()` removes that cache's
  entries. Hits/misses/invalidations/evictions accumulate over the handle's life.
- `Dispose()` retires a handle and releases its ID. Old handles cannot remove
  replacements. Disposal of the registry retires every handle and its telemetry
  registration. Factory reentrancy must not partially retire the store.
- Cache values are derived data, not owned Unity resources. Eviction/disposal
  drops references, it does not call `Dispose()`/`UnityEngine.Object.Destroy()` on
  values. Resource ownership requires a separate domain contract.

The registry is created during library setup. `BindCurrentThread()` binds actual
cache use at the existing Controller `OnUpdate` main-thread boundary. Setup may
register handles before binding; lookups require binding. After binding, all
operations except invalidation requests require the owner thread. This is not a
general concurrent worker cache and does not pretend Unity calls are thread-safe.

`InvalidateAll()` is a thread-safe generation request for async def reloads.
Each handle applies it on its next owner-thread read or statistics publication.
No UI calculation or dictionary mutation is run by the loading thread. This
means an inactive cache may retain references until its next access/publication
or disposal. The cache does not run its own timer or thread.

## Settings cutover

`TextMeasurementCache` is a small engine adapter over this cache. Its key includes
current title text, exact float width, font, UI scale and language. A miss restores
the previous Verse font even if measurement fails. Its capacity is 512 entries.
Defs reload requests invalidation for custom changes to underlying font data.

`Dialog_ModSettings` now reads the current title and asks this shared cache for
height. `CachedLabel`, its unused size/translation APIs and per-control title
memoization are removed. There is no fallback to the old cache.

This fixes the previous `(int)width` collision and stale title/font keys. It does
not claim to cache arbitrary mutable GUIStyle changes made without invalidation.

## Telemetry and verification

`fixworld.caches` schema 1 uses the same typed telemetry store/presentation
contract as the library. It exports only cache identity and counts, never cached
keys/values. Controller publication schedules it with the existing 500 ms
diagnostics cadence. Provider snapshots are detached read-only values.

Run `dotnet run --project mod/FixWorld/Tests/Caching.Contracts/FixWorld.Caching.Contracts.csproj -c Release`.
This exercises production cache and text-adapter source with deterministic engine
stubs, including width/font/title/scale/language keys, invalidation, bounds,
failure, thread affinity, disposal and shared telemetry/profiling.
The full fork also needs the [local-reference build](telemetry.md#verification).
Desktop CLR timings and compilation are not in-game validation.

## Deliberately not migrated here

News images use a window-owned `NewsImageSet` with explicit owned/borrowed texture
results, not the generic derived-data cache. Only file-loaded textures are
destroyed on replacement or window close; ContentFinder and placeholder assets
remain borrowed. See [News image lifetime](news.md).
Resolved FieldInfo bindings are engine contracts, not a reason to add runtime
dictionary lookups around every reflection handle.
