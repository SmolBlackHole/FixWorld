using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FixWorld.Textures;

internal static class Program
{
    private static int checks;

    private static void Main(string[] args)
    {
        if (ConverterContracts.TryRunFixture(args))
            return;
        string root = Path.Combine(Path.GetTempPath(), "FixWorld-Dds-Contracts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Storage(root);
            IndexWrite(root);
            Dimensions(root);
            Payload();
            Progress();
            BudgetAndSettingsContracts.Run(root, Check);
            ConverterContracts.Run(root, Check);
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"PASS: {checks} DDS storage, payload and converter-process contracts. No Unity or real texture conversion.");
    }

    private static void Progress()
    {
        var data = new TextureDdsCacheSnapshot(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
            plannedMods: 5, processedMods: 2, currentMod: "fixture.mod");
        Check(data.RemainingMods == 3 && data.CurrentMod == "fixture.mod", "Progress retains current mod and remaining count");
        Check(TextureDdsCacheSnapshot.Disabled(0).RemainingMods == 0, "Disabled progress is empty");
        using var store = new FixWorld.Telemetry.TelemetryStore();
        using var registration = store.Register(TextureDdsCacheSnapshot.Contract);
        registration.Publish(data);
        using var output = new StringWriter();
        store.WriteJson(output);
        Check(output.ToString().Contains("batch_remaining_mods") && output.ToString().Contains("fixture.mod"), "Progress uses shared JSON export");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        checks++;
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try
        { action(); }
        catch (T) { checks++; return; }
        throw new InvalidOperationException(message);
    }

    private static void Storage(string root)
    {
        string cache = Path.Combine(root, "cache");
        string sourcePath = Path.Combine(root, "source.png");
        File.WriteAllBytes(sourcePath, new byte[32]);
        FileInfo source = new FileInfo(sourcePath);
        string packPath;
        using (DdsPackStore store = DdsPackStore.Open(cache, DdsCacheContract.CacheIdentityVersion))
        {
            Check(store.EntryCount == 0 && store.LoadStatus == "new pack index", "new index");
            Throws<IOException>(() => DdsPackStore.Open(cache, DdsCacheContract.CacheIdentityVersion), "exclusive writer lock");
            DdsPackSnapshot empty = store.Snapshot();
            DdsBuiltPack pack = Pack(store, "Mod.A", "one", source, 128);
            store.Publish(pack);
            Check(store.EntryCount == 1 && store.CurrentBytes == 128, "published pack accounting");
            foreach ((long Offset, long Length) invalid in new[] { (-1L, 16L), (0L, 0L), (64L, 65L), (long.MaxValue, 1L) })
            {
                DdsBuiltPack malformed = Pack(store, "Mod.A", "malformed", source, 128);
                malformed = new DdsBuiltPack(malformed.PackageId, malformed.Generation,
                    malformed.StagingRoot, malformed.TemporaryPath, new[]
                    {
                        new DdsBuiltEntry("textures/source.png", source, null, "converter", invalid.Offset, invalid.Length)
                    });
                Throws<InvalidDataException>(() => store.Publish(malformed), "invalid generated slice refused before publication");
                Check(store.EntryCount == 1 && File.Exists(malformed.TemporaryPath), "invalid publication preserves active index and staged file");
                store.Discard(malformed);
            }
            Check(!empty.TryGetFresh("mod.a", "textures/source.png", source, "converter", out _), "snapshot immutable after publish");
            DdsPackSnapshot snapshot = store.Snapshot();
            Check(snapshot.TryGetFresh("MOD.A", "Textures\\Source.png", source, "converter", out DdsPackSlice slice), "normalized fresh lookup");
            packPath = slice.Path;
            Check(slice.Offset == 16 && slice.Length == 112, "slice offset and length preserved");
            Check(!snapshot.TryGetFresh("mod.b", "textures/source.png", source, "converter", out _), "mod identity isolated");
            Check(!snapshot.TryGetFresh("mod.a", "textures/source.png", source, "new-converter", out _), "converter mismatch invalidates");
            Check(snapshot.TryGetFresh("mod.a", "textures/source.png", source, null, out _), "cache remains readable without installed converter");
            File.SetLastWriteTimeUtc(sourcePath, source.LastWriteTimeUtc.AddSeconds(2));
            Check(!snapshot.TryGetFresh("mod.a", "textures/source.png", new FileInfo(sourcePath), "converter", out _), "source timestamp invalidates");
            File.SetLastWriteTimeUtc(sourcePath, source.LastWriteTimeUtc);
            File.WriteAllBytes(sourcePath, new byte[33]);
            Check(!snapshot.TryGetFresh("mod.a", "textures/source.png", new FileInfo(sourcePath), "converter", out _), "source length invalidates");
            File.WriteAllBytes(sourcePath, new byte[32]);
            File.SetLastWriteTimeUtc(sourcePath, source.LastWriteTimeUtc);
            store.Save();
            Check(File.Exists(Path.Combine(cache, DdsCacheContract.IndexFileName)), "manifest exists");
            store.TouchPackages(new HashSet<string> { "mod.a" });
            Check(store.EntryCount == 1, "touch preserves entries");
            string orphan = Path.Combine(cache, "orphan.fwdp");
            File.WriteAllBytes(orphan, new byte[8]);
            Check(store.SweepOrphans() == 1 && !File.Exists(orphan), "orphan removed, active pack preserved");
        }

        using (DdsPackStore reopened = DdsPackStore.Open(cache, DdsCacheContract.CacheIdentityVersion))
        {
            Check(reopened.LoadStatus == "loaded" && reopened.EntryCount == 1, "persistent manifest roundtrip");
            Check(reopened.Snapshot().TryGetFresh("mod.a", "textures/source.png", new FileInfo(sourcePath), "converter", out _), "roundtrip fresh lookup");
            string staging = reopened.CreateStagingRoot("mod.a");
            reopened.DiscardStaging(staging);
            Check(!Directory.Exists(staging), "cancelled staging removed");
            Throws<InvalidOperationException>(() => reopened.DiscardStaging(root), "cleanup rejects outside cache");
            Check(!DdsPackStore.TryResolvePackPath(cache, "../escape.fwdp", out _), "parent traversal rejected");
            Check(!DdsPackStore.TryResolvePackPath(cache, packPath, out _), "absolute manifest pack path rejected");
            Check(!DdsPackStore.TryResolvePackPath(cache, "index.json", out _), "non-pack extension rejected");
            Check(reopened.ReconcilePackages(new Dictionary<string, HashSet<string>>
            {
                ["mod.a"] = new HashSet<string>()
            }) == 1, "deleted source reconciled");
            reopened.Save();
            Check(File.Exists(Path.Combine(cache, DdsCacheContract.BackupFileName)), "atomic replace preserves backup");
        }

        File.WriteAllText(Path.Combine(cache, DdsCacheContract.IndexFileName), "{broken");
        using (DdsPackStore backup = DdsPackStore.Open(cache, DdsCacheContract.CacheIdentityVersion))
        {
            Check(backup.EntryCount == 1 && backup.LoadStatus == "loaded", "corrupt manifest recovered from backup");
            File.WriteAllBytes(packPath, new byte[20]);
            Check(!backup.Snapshot().TryGetFresh("mod.a", "textures/source.png", new FileInfo(sourcePath), "converter", out _), "truncated pack refused");
            File.Delete(packPath);
            Check(!backup.Snapshot().TryGetFresh("mod.a", "textures/source.png", new FileInfo(sourcePath), "converter", out _), "missing pack refused");
        }
        using (DdsPackStore incompatible = DdsPackStore.Open(cache, "new-format"))
            Check(incompatible.EntryCount == 0, "cache identity migration starts empty");

        string budgetRoot = Path.Combine(root, "budget");
        using (DdsPackStore budget = DdsPackStore.Open(budgetRoot, DdsCacheContract.CacheIdentityVersion))
        {
            budget.Publish(Pack(budget, "a", "a", new FileInfo(sourcePath), 128));
            budget.Publish(Pack(budget, "b", "b", new FileInfo(sourcePath), 128));
            Check(budget.EntryCount == 2, "two independent package packs");
            Check(budget.EnforceBudget(128) == 1 && budget.CurrentBytes == 128, "budget evicts complete oldest pack");
            Check(budget.RemoveInactivePackages(new HashSet<string>()) == 1, "inactive package removed");
            budget.Save();
            budget.SweepOrphans();
            Check(budget.CurrentBytes == 0, "unreferenced packs reclaimed");
        }
    }

    private static DdsBuiltPack Pack(DdsPackStore store, string package, string generation, FileInfo source, int size)
    {
        string staging = store.CreateStagingRoot(package);
        string temporary = Path.Combine(staging, "pack.tmp");
        File.WriteAllBytes(temporary, new byte[size]);
        return new DdsBuiltPack(package, generation, staging, temporary, new[]
        {
            new DdsBuiltEntry("textures/source.png", source, DdsCacheKey.HashFile(source.FullName), "converter", 16, size - 16)
        });
    }

    private static void IndexWrite(string root)
    {
        string path = Path.Combine(root, "atomic.json");
        File.WriteAllText(path, "old");
        Throws<IOException>(() => DdsIndexFile.Write(path, stream =>
        {
            stream.WriteByte(1);
            throw new IOException("fixture write failure");
        }, path + ".backup"), "write error propagated");
        Check(File.ReadAllText(path) == "old", "failed index write preserves original");
        Check(!Directory.EnumerateFiles(root, "atomic.json.tmp-*").Any(), "temporary index removed after failure");
    }

    private static void Dimensions(string root)
    {
        string path = Path.Combine(root, "dimensions.png");
        byte[] png = new byte[24];
        png[0] = 137;
        png[1] = 80;
        png[2] = 78;
        png[3] = 71;
        png[19] = 64;
        png[23] = 32;
        File.WriteAllBytes(path, png);
        Check(TextureDimensions.TryRead(new FileInfo(path), out TextureDimensions dimensions), "PNG dimensions read without decoding");
        Check(dimensions.Width == 64 && dimensions.Height == 32, "PNG dimensions values");
        Check(dimensions.GetBlockCompressedMipCount() == 2, "partial BC mip chain preserved");
        Check(dimensions.GetBc7FileSize(2) == 148 + 2048 + 512, "BC7 payload estimate");
        png[23] = 16;
        File.WriteAllBytes(path, png);
        TextureDimensions.TryRead(new FileInfo(path), out dimensions);
        Check(dimensions.GetBlockCompressedMipCount() == 0, "tiny UI textures excluded");
        png[23] = 31;
        File.WriteAllBytes(path, png);
        TextureDimensions.TryRead(new FileInfo(path), out dimensions);
        Check(dimensions.GetBlockCompressedMipCount() == 0, "unaligned texture excluded");
        File.WriteAllBytes(path, new byte[4]);
        Check(!TextureDimensions.TryRead(new FileInfo(path), out _), "truncated PNG refused");
    }

    private static void Payload()
    {
        const long twoMips = 148 + 2048 + 512;
        Check(DdsPayload.Validate(twoMips + 16, 16, twoMips, 64, 32, 2) == 2560, "valid partial BC7 chain");
        Check(DdsPayload.Validate(148 + 96 + 32, 0, 148 + 96 + 32, 12, 8, 2) == 128, "NPOT partial chain uses ceil block count");
        Check(DdsPayload.Validate(148 + 16 * 3, 0, 148 + 16 * 3, 4, 4, 3) == 48, "full chain includes sub-block mips");
        Check(DdsPayload.Validate(148 + 16384L * 16384L, 0, 148 + 16384L * 16384L, 16384, 16384, 1) == 268435456, "maximum permitted dimensions");
        Throws<InvalidDataException>(() => DdsPayload.Validate(-1, 0, twoMips, 64, 32, 2), "negative file size refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips, -1, twoMips, 64, 32, 2), "negative offset refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(147, 0, 147, 64, 32, 2), "truncated DX10 header refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips, 1, twoMips, 64, 32, 2), "slice extending past file refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(long.MaxValue, long.MaxValue, 148, 64, 32, 2), "offset addition overflow refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips, 0, long.MaxValue, 64, 32, 2), "oversized length refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips, 0, twoMips, 0, 32, 2), "zero dimensions refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips, 0, twoMips, 16388, 32, 2), "oversized dimensions refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips, 0, twoMips, 63, 32, 2), "non-block-aligned base refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips, 0, twoMips, 64, 32, 0), "zero mips refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips, 0, twoMips, 64, 32, 8), "mips beyond complete chain refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips - 1, 0, twoMips - 1, 64, 32, 2), "truncated payload refused");
        Throws<InvalidDataException>(() => DdsPayload.Validate(twoMips + 1, 0, twoMips + 1, 64, 32, 2), "trailing payload refused");
    }
}
