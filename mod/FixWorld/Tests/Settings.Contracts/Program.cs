using System;
using System.Collections;
using System.Reflection;
using FixWorld;
using FixWorld.Settings;
using UnityEngine;
using Verse;

internal static class Program
{
    private static int checks;
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    private static int Main()
    {
        var pack = new ModSettingsPack("settings-test") { EntryName = "Test" };
        var cache = pack.GetHandle("cache", "Cache", "", 6, Validators.IntRangeValidator(0, 100));
        var other = pack.GetHandle("other", "Other", "", 3);
        int baseline = Subscribers(cache);
        var panel = new SettingsPanel(pack, h => ReferenceEquals(h, cache));
        Require(Subscribers(cache) == baseline + 1, "One subscription per panel");
        Require(Subscribers(other) == 0, "Filtered handles are not observed");
        Require(panel.DrawContents(new Rect(0, 0, 500, 500)) == 34, "Only selected handle rendered");

        Widgets.Input = "10";
        panel.DrawContents(new Rect(0, 0, 500, 500));
        Require(cache.Value == 6, "Focused text waits for commit");
        panel.CommitPending();
        Require(cache.Value == 10 && cache.HasUnsavedChanges, "Page switch commits valid pending value");
        panel.ScrollY = 42;
        cache.Value = 12;
        Require(panel.ScrollY == 42, "External value refresh retains scroll");
        Require(Input(panel, cache) == "12", "External changes update field value");
        panel.ScrollY = 0;

        Widgets.Input = "-1";
        panel.DrawContents(new Rect(0, 0, 500, 500));
        panel.CommitPending();
        Require(cache.Value == 12 && Input(panel, cache) == "-1", "Invalid text retained without changing typed value");

        other.Value = 9;
        panel.ResetToDefaults();
        ((FixWorld.Utils.Dialog_Confirm)Find.WindowStack.Last).Confirm();
        Require(cache.Value == 6 && other.Value == 9, "Reset respects selected handle scope");
        Require(Input(panel, cache) == "6", "Reset refreshes control");

        Widgets.Input = "20";
        panel.DrawContents(new Rect(0, 0, 500, 500));
        panel.Dispose();
        Require(cache.Value == 20, "Close commits pending valid input");
        Require(Subscribers(cache) == baseline, "Close removes subscriptions");
        panel.Dispose();
        Require(Subscribers(cache) == baseline, "Dispose is idempotent");
        cache.Value = 21;
        Require(Input(panel, cache) == "20", "Closed panel does not observe changes");
        Require(FixWorldController.Logger.Errors == 0, "No renderer exceptions");
        Console.WriteLine("PASS: " + checks + " settings-panel contracts. Production renderer; Unity drawing stubbed.");
        return 0;
    }
    private static object Info(SettingsPanel panel, SettingHandle handle) =>
        ((IDictionary)typeof(SettingsPanel).GetField("handleControlInfo", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel))[handle];
    private static string Input(SettingsPanel panel, SettingHandle handle)
    { var info = Info(panel, handle); return (string)info.GetType().GetField("InputValue").GetValue(info); }
    private static int Subscribers(SettingHandle handle)
    {
        var value = (Delegate)typeof(SettingHandle).GetField("ValueChanged", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(handle);
        return value?.GetInvocationList().Length ?? 0;
    }
}
