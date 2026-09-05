using System;
using System.Collections.Generic;
using System.IO;
using FixWorld.News;
using UnityEngine;
using Verse;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        ++checks;
    }

    private static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "FixWorld-News-Contracts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { Run(root); }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"PASS: {checks} news image contracts. Production loader and owner, stubbed Unity operations. No game started.");
    }

    private static void Run(string root)
    {
        var a = new ModContentPack { RootDir = Path.Combine(root, "a"), PackageIdPlayerFacing = "a" };
        var b = new ModContentPack { RootDir = Path.Combine(root, "b"), PackageIdPlayerFacing = "b" };
        foreach (var mod in new[] { a, b })
        {
            Directory.CreateDirectory(Path.Combine(mod.RootDir, "News"));
            File.WriteAllBytes(Path.Combine(mod.RootDir, "News", "same.png"), new byte[] { 1 });
        }
        File.WriteAllBytes(Path.Combine(a.RootDir, "News", "invalid.png"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(a.RootDir, "News", "throws.png"), new byte[] { 2 });
        var placeholder = new Texture2D(2, 2, TextureFormat.Alpha8, false);
        var shared = new Texture2D(2, 2, TextureFormat.Alpha8, false);
        ContentFinder<Texture2D>.Assets.Add(BaseContent.BadTexPath, placeholder);
        ContentFinder<Texture2D>.Assets.Add("shared", shared);
        ContentFinder<Texture2D>.Assets.Add("same", shared);
        var queue = new Queue<Action>();
        var images = new NewsImageSet();
        images.Replace(new[] { (a, "same"), (a, "same"), (b, "same"), (a, "shared"), (a, "missing"), (b, "missing") }, queue.Enqueue);
        Check(images.Pending && queue.Count == 1, "deferred load");
        Check(!images.TryGet(a, "same", out _), "not loaded early");
        int created = Texture2D.Created.Count;
        queue.Dequeue()();
        Check(!images.Pending, "pending cleared after load");
        Check(Texture2D.Created.Count == created + 2, "duplicate requests load once per mod");
        Check(images.TryGet(a, "same", out var ownA), "first mod loaded");
        Check(images.TryGet(b, "same", out var ownB) && !ReferenceEquals(ownA, ownB), "same filename isolated across mods");
        Check(!ReferenceEquals(ownA, shared), "news file wins over borrowed fallback");
        Check(images.TryGet(a, "shared", out var borrowed) && ReferenceEquals(borrowed, shared), "shared fallback borrowed");
        Check(images.TryGet(a, "missing", out var missing) && ReferenceEquals(missing, placeholder), "placeholder borrowed");
        images.Reset();
        images.Reset();
        Check(ownA.DestroyCalls == 1 && ownB.DestroyCalls == 1, "owned textures destroyed exactly once");
        Check(shared.DestroyCalls == 0 && placeholder.DestroyCalls == 0, "borrowed textures never destroyed");
        Check(!images.TryGet(a, "same", out _), "reset drops lookup refs");

        images.Replace(new[] { (a, "same") }, queue.Enqueue);
        created = Texture2D.Created.Count;
        images.Reset(); // Window closed before the queued callback.
        queue.Dequeue()();
        Check(Texture2D.Created.Count == created && !images.Pending, "closed window cannot load late images");
        images.Replace(new[] { (a, "same") }, queue.Enqueue);
        images.Replace(new[] { (b, "same") }, queue.Enqueue);
        queue.Dequeue()();
        Check(images.Pending && !images.TryGet(a, "same", out _), "stale callback cannot clear newer pending batch");
        queue.Dequeue()();
        Check(images.TryGet(b, "same", out var replacement) && !images.Pending, "replacement batch loads");
        images.Replace(Array.Empty<(ModContentPack, string)>(), queue.Enqueue);
        Check(replacement.DestroyCalls == 1 && !images.Pending && queue.Count == 0, "text-only replacement releases previous images");

        images.Replace(new[] { (a, "invalid"), (a, "throws") }, queue.Enqueue);
        created = Texture2D.Created.Count;
        queue.Dequeue()();
        Check(Texture2D.Created.Count == created + 2, "failure fixtures allocate textures");
        Check(Texture2D.Created[created].DestroyCalls == 1, "false decode result releases texture");
        Check(Texture2D.Created[created + 1].DestroyCalls == 1, "processing exception releases texture");
        Check(images.TryGet(a, "invalid", out var invalid) && ReferenceEquals(invalid, placeholder), "invalid image uses placeholder");
        images.Reset();
        Check(placeholder.DestroyCalls == 0, "failure fallback remains borrowed");
        try { images.Replace(new[] { (a, "same") }, _ => throw new InvalidOperationException()); }
        catch (InvalidOperationException) { }
        Check(!images.Pending, "enqueue failure clears pending state");
    }
}
