// SPDX-License-Identifier: MPL-2.0
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FixWorld.Caching;
using FixWorld.Profiling;
using FixWorld.Telemetry;
using FixWorld.Settings;
using Verse;

internal static class Program
{
    private static int checks;
    private static void Main()
    {
        using var diagnostics = new LibraryDiagnostics();
        using var store = new CacheStore(diagnostics.Store, diagnostics.Profiler);
        int created = 0;
        var unused = store.Create(new CacheContract<int, int>("unused", 1, x => x));
        unused.Dispose();
        var cache = store.Create(new CacheContract<int, string>("test", 2, key => { created++; return key == 0 ? null : key.ToString(); }));
        Expect<InvalidOperationException>(() => cache.GetOrAdd(1));
        store.BindCurrentThread(); store.BindCurrentThread();
        store.Publish(); Require(store.Snapshot.Caches.Count == 1, "Unused cache can retire before binding");
        Expect<ArgumentException>(() => new CacheContract<int, int>(" ", 1, x => x));
        Expect<ArgumentOutOfRangeException>(() => new CacheContract<int, int>("x", 0, x => x));
        Expect<InvalidOperationException>(() => store.Create(new CacheContract<int, int>("test", 1, x => x)));
        Require(cache.GetOrAdd(0) == null && cache.GetOrAdd(0) == null && created == 1, "Null is a cacheable result");
        cache.GetOrAdd(1); cache.GetOrAdd(0); cache.GetOrAdd(2);
        Require(!cache.TryGet(0, out _) && cache.Count == 2, "Bounded FIFO, hits do not reorder");
        Require(cache.Invalidate(1) && !cache.Invalidate(1), "Key invalidation");
        cache.GetOrAdd(1); cache.GetOrAdd(3);
        Require(cache.TryGet(1, out _) && !cache.TryGet(2, out _), "Reinsert after invalidation has no stale FIFO node");
        store.Publish(); var previous = store.Snapshot;
        diagnostics.Profiler.SetEnabled(false); cache.GetOrAdd(3);
        store.Publish();
        Require(store.Snapshot.Caches[0].Hits == previous.Caches[0].Hits + 1, "Business counts continue without profiler");
        Task.Run(() => Expect<InvalidOperationException>(() => cache.GetOrAdd(4))).GetAwaiter().GetResult();
        Task.Run(() => Expect<InvalidOperationException>(() => store.BindCurrentThread())).GetAwaiter().GetResult();
        Task.Run(() => store.InvalidateAll()).GetAwaiter().GetResult();
        Require(cache.Count == 0 && previous.Caches[0].Count == 2, "Background invalidation applied on owner, old DTO retained");
        bool fail = true;
        diagnostics.Profiler.SetEnabled(true);
        var failure = store.Create(new CacheContract<int, int>("failure", 1, key => fail ? throw new Exception("factory") : key));
        Expect<Exception>(() => failure.GetOrAdd(1));
        Require(failure.Count == 0, "Failed result is not cached"); fail = false;
        Require(failure.GetOrAdd(1) == 1, "Factory can retry");
        Require(diagnostics.Profiler.PublishSnapshot().TryGet(new ProfileKey("failure", "cache.create"), out var failedFactory)
            && failedFactory.Failures == 1 && failedFactory.Calls == 2, "Failed and successful creation use the shared profiler");
        failure.Clear(); Require(failure.Count == 0, "Explicit clear");
        TypedCache<int, int> recursive = null;
        recursive = store.Create(new CacheContract<int, int>("recursive", 1, key => recursive.GetOrAdd(key + 1)));
        Expect<InvalidOperationException>(() => recursive.GetOrAdd(1));
        Require(recursive.Count == 0, "Recursive factory rejected without publication");
        var destroyOwner = store.Create(new CacheContract<int, int>("destroy-owner", 1, key => { store.Dispose(); return key; }));
        Expect<InvalidOperationException>(() => destroyOwner.GetOrAdd(1));
        Require(cache.GetOrAdd(10) == "10", "Rejected reentrant disposal does not partly retire caches");
        failure.Dispose(); failure.Dispose();
        var replacement = store.Create(new CacheContract<int, int>("failure", 1, x => x));
        failure.Dispose(); Require(replacement.GetOrAdd(2) == 2, "Old handle cannot remove replacement");
        Expect<ObjectDisposedException>(() => failure.GetOrAdd(2));
        Labels(store);
        var hit = store.Create(new CacheContract<int, object>("hit-smoke", 1, _ => new object()));
        var item = hit.GetOrAdd(1); var timer = Stopwatch.StartNew(); int gc = GC.CollectionCount(0);
        for (int i = 0; i < 100000; i++) if (!ReferenceEquals(item, hit.GetOrAdd(1))) throw new Exception("Copy on hit");
        timer.Stop(); Console.WriteLine($"Cache hit smoke: {timer.Elapsed.TotalMilliseconds:F2} ms / 100000, Gen0 delta {GC.CollectionCount(0) - gc}. Desktop CLR only.");
        store.Dispose(); store.Dispose();
        Expect<ObjectDisposedException>(() => replacement.GetOrAdd(3));
        Require(diagnostics.Store.Registrations.Count == 1, "Cache store unregisters telemetry, library remains");
        Console.WriteLine($"PASS: {checks} cache contracts. Actual adapter tested with deterministic engine stubs.");
    }
    private static void Labels(CacheStore store)
    {
        var labels = new TextMeasurementCache(store);
        labels.Height("title", 100.1f, GameFont.Small); int calls = Text.Calls;
        labels.Height("title", 100.1f, GameFont.Small);
        Require(Text.Calls == calls && Text.Font == GameFont.Medium, "Shared label hit, font state restored");
        labels.Height("title", 100.9f, GameFont.Small);
        Require(Text.Calls == ++calls, "Fractional widths do not collide");
        labels.Height("title", 100.9f, GameFont.Tiny);
        Require(Text.Calls == ++calls, "Font is in key");
        labels.Height("new title", 100.9f, GameFont.Tiny);
        Require(Text.Calls == ++calls, "Changed title is not stale");
        Prefs.UIScale = 1.5f; labels.Height("new title", 100.9f, GameFont.Tiny);
        Require(Text.Calls == ++calls, "Scale is in key");
        Prefs.LangFolderName = "German"; labels.Height("new title", 100.9f, GameFont.Tiny);
        Require(Text.Calls == ++calls, "Language is in key");
        store.InvalidateAll(); labels.Height("new title", 100.9f, GameFont.Tiny);
        Require(Text.Calls == ++calls, "Defs invalidation reaches cached labels");
        Text.Throw = true;
        Expect<InvalidOperationException>(() => labels.Height("failure", 50, GameFont.Small));
        Require(Text.Font == GameFont.Medium, "Failed engine call restores font");
        Text.Throw = false; labels.Height("failure", 50, GameFont.Small);
        for (int i = 0; i < 600; i++) labels.Height("many labels", i, GameFont.Small);
        store.Publish();
        foreach (var stats in store.Snapshot.Caches) if (stats.Id == "settings.text_height")
            Require(stats.Count == 512 && stats.Evictions > 0, "Real adapter bounded to 512 entries");
    }
    private static void Require(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
    private static void Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
}
