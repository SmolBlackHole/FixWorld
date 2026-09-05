# News image lifetime

Parent: [Typed caches](caching.md)

The inherited News window already displays versioned `UpdateFeatureDef` entries,
including text, images and links. This can carry FixWorld changelog entries;
writing those entries is separate from resource management.

`UpdateFeatureImageLoader` returns a texture plus explicit ownership. Images
created from files in a mod's News folder are owned. Textures returned by
ContentFinder, including the missing-image placeholder, are borrowed. A failed
decode or post-processing step destroys the partially created texture before
falling back. A false `LoadImage` result is a failure, not a valid image.

`NewsImageSet` belongs to one window and keys images by ModContentPack and filename.
Repeated requests within the same mod load once; identical names in different
mods remain distinct. It is a Unity-resource owner, not a generic data cache.

Loading still uses the existing next-Update callback on the main thread. Replacing
the displayed news or closing the window invalidates queued batches. Old callbacks
do not create textures or change a newer batch's pending state. A text-only
replacement also clears pending state. Reset releases owned textures exactly once
and drops all lookup references; borrowed assets are never destroyed.

Window cleanup uses `PostClose`, including direct WindowStack removal. Closing also
drops the segment lists holding rendering references. Filtering/reloading replaces
the segments and image set together. No new worker, timer or scheduler is involved.

## Verification

Run `dotnet run --project mod/FixWorld/Tests/News.Contracts/FixWorld.News.Contracts.csproj -c Release`.
The tests compile the production loader and image owner against deterministic
Unity/Verse stubs. They test ownership, mod isolation, duplicate requests, deferred
load invalidation, replacement, decode failure, processing failure and queue failure.
Full-fork compilation checks the real dialog integration against local game DLLs.
These checks do not exercise Unity's actual decoder/rendering or an in-game window.
