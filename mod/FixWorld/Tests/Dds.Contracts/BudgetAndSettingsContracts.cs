using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FixWorld.Settings;
using FixWorld.Textures;

internal static class BudgetAndSettingsContracts
{
    internal static void Run(string root, Action<bool, string> check)
    {
        long gib = DdsBudget.GiB;
        check(DdsBudget.EffectiveMaximum(6 * gib, 2 * gib, 20 * gib, 10 * gib) == 6 * gib, "plenty of drive space permits configured cache cap");
        check(DdsBudget.EffectiveMaximum(6 * gib, 2 * gib, 9 * gib, 10 * gib) == gib, "free reserve shrinks cache below maximum");
        check(DdsBudget.EffectiveMaximum(6 * gib, 2 * gib, 4 * gib, 10 * gib) == 0, "unachievable reserve requires empty cache");
        check(DdsBudget.EffectiveMaximum(6 * gib, 0, 4 * gib, 10 * gib) == 0, "empty cache cannot consume unavailable reserve");
        check(DdsBudget.EffectiveMaximum(0, 2 * gib, 20 * gib, 10 * gib) == 0, "zero configured limit means evict all");
        check(DdsBudget.EffectiveMaximum(long.MaxValue, long.MaxValue, long.MaxValue, 0) == long.MaxValue, "budget calculation saturates instead of overflowing");

        string cache = Path.Combine(root, "reserve");
        string sourcePath = Path.Combine(root, "reserve-source.png");
        File.WriteAllBytes(sourcePath, new byte[32]);
        using (DdsPackStore store = DdsPackStore.Open(cache, DdsCacheContract.CacheIdentityVersion))
        {
            Publish(store, new FileInfo(sourcePath), "one");
            Publish(store, new FileInfo(sourcePath), "two");
            long effective = DdsBudget.EffectiveMaximum(4096, store.CurrentBytes, 896, 1024);
            check(effective == 128 && store.EnforceBudget(effective) == 1 && store.CurrentBytes == 128,
                "real pack eviction satisfies free reserve while far below cap");
            check(store.EnforceBudget(0) == 1 && store.EntryCount == 0 && store.CurrentBytes == 0,
                "real zero budget reclaims all pack bytes");
        }

        string oldMaximum = Environment.GetEnvironmentVariable(DdsSettings.MaximumEnvironmentVariable);
        string oldMinimum = Environment.GetEnvironmentVariable(DdsSettings.MinimumFreeEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DdsSettings.MaximumEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(DdsSettings.MinimumFreeEnvironmentVariable, null);
            ModSettingsPack pack = new ModSettingsPack("FixWorld");
            int changed = 0;
            DdsSettings settings = new DdsSettings(pack, () => changed++);
            check(settings.MaximumGiB.Value == 6 && settings.MinimumFreeGiB.Value == 10, "typed DDS setting defaults are6/10GiB");
            check(settings.Owns(settings.MaximumGiB) && settings.Owns(settings.MinimumFreeGiB), "UI filters exact DDS handles");
            check(settings.EffectiveMaximumBytes == 6 * gib && settings.EffectiveMinimumFreeBytes == 10 * gib, "stored defaults feed byte policy");
            settings.MaximumGiB.StringValue = "3";
            settings.MinimumFreeGiB.StringValue = "12";
            check(changed == 2 && pack.HasUnsavedChanges, "setting changes dispatch existing handle event");
            XElement xml = new XElement("settings");
            pack.WriteXml(xml);
            ModSettingsPack restoredPack = new ModSettingsPack("FixWorld");
            restoredPack.LoadFromXml(xml.Element("FixWorld"));
            using (DdsSettings restored = new DdsSettings(restoredPack, () => { }))
                check(restored.MaximumGiB.Value == 3 && restored.MinimumFreeGiB.Value == 12, "existing HugsLib XML persistence roundtrip");
            settings.MaximumGiB.StringValue = "-1";
            check(settings.MaximumGiB.Value == 6, "negative setting resets through existing validator");
            settings.MaximumGiB.StringValue = "0";
            check(settings.MaximumGiB.Value == 0 && settings.EffectiveMaximumBytes == 0, "zero cache setting permitted");
            settings.MaximumGiB.ResetToDefault();
            settings.MinimumFreeGiB.ResetToDefault();
            check(settings.MaximumGiB.Value == 6 && settings.MinimumFreeGiB.Value == 10, "existing reset restores DDS defaults");
            Environment.SetEnvironmentVariable(DdsSettings.MaximumEnvironmentVariable, "1.5");
            Environment.SetEnvironmentVariable(DdsSettings.MinimumFreeEnvironmentVariable, "0");
            check(settings.EffectiveMaximumBytes == gib + gib / 2 && settings.EffectiveMinimumFreeBytes == 0,
                "environment overrides support fractional and zero GiB");
            check(settings.MaximumOverridden && settings.MinimumFreeOverridden && settings.MaximumGiB.Value == 6,
                "effective override never overwrites stored setting");
            int beforeDispose = changed;
            settings.Dispose();
            settings.MaximumGiB.Value = 9;
            check(changed == beforeDispose, "settings lifetime detaches callback");
        }
        finally
        {
            Environment.SetEnvironmentVariable(DdsSettings.MaximumEnvironmentVariable, oldMaximum);
            Environment.SetEnvironmentVariable(DdsSettings.MinimumFreeEnvironmentVariable, oldMinimum);
        }
    }

    private static void Publish(DdsPackStore store, FileInfo source, string package)
    {
        string staging = store.CreateStagingRoot(package);
        string temporary = Path.Combine(staging, "pack.tmp");
        File.WriteAllBytes(temporary, new byte[128]);
        store.Publish(new DdsBuiltPack(package, "one", staging, temporary, new List<DdsBuiltEntry>
        {
            new DdsBuiltEntry("source.png", source, null, "converter", 16, 112)
        }));
    }
}
