using System;
using System.IO;
using FixWorld.Telemetry;
using FixWorld.UI;

internal static class LoadingContracts
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value)
        { if (!value) throw new Exception("Loading contract " + checks); checks++; }
        long time = 100;
        using var store = new TelemetryStore();
        using var loading = new LoadingProgress(store, () => time, 1000);
        Check(!loading.Transition(LoadingStage.Mods));
        loading.Begin();
        var first = loading.Current;
        time += 25;
        Check(loading.Transition(LoadingStage.Mods));
        Check(loading.Current.Duration(0) == 25 && first.Duration(0) == 0);
        Check(!loading.Transition(LoadingStage.Mods) && !loading.Transition(LoadingStage.Reset));
        time += 75;
        Check(loading.Elapsed(loading.Current) == 100);
        loading.Transition(LoadingStage.Complete);
        Check(!loading.Current.Active && loading.Current.ElapsedMilliseconds == 100);
        time += 9000;
        Check(loading.Elapsed(loading.Current) == 100);
        using var json = new StringWriter();
        store.WriteJson(json);
        Check(json.ToString().Contains("fixworld.loading") && json.ToString().Contains("\"Reset_ms\":25"));
        loading.Begin();
        Check(loading.Current.Active && loading.Current.Duration(0) == 0);
        loading.Transition(LoadingStage.Classes);
        loading.CrossReferences(false);
        Check(loading.Current.Stage == LoadingStage.Classes);
        loading.Transition(LoadingStage.Xml);
        loading.CrossReferences(true);
        Check(loading.Current.Stage == LoadingStage.Xml);
        loading.Transition(LoadingStage.Import);
        loading.CrossReferences(true);
        Check(loading.Current.Stage == LoadingStage.Bind);
        loading.Transition(LoadingStage.PreImplied);
        loading.CrossReferences(false);
        Check(loading.Current.Stage == LoadingStage.CrossReferences);
        loading.Fail(new Exception("fixture"));
        Check(!loading.Current.Active && loading.Current.Failure == "fixture");
        Check(LoadingTips.Get(0) == LoadingTips.Get(7999));
        Check(LoadingTips.Get(0) != LoadingTips.Get(8000));
        Check(LoadingTips.Get(-100) == LoadingTips.Get(0));
        for (int i = 0; i < LoadingProgress.Names.Length; i++)
        {
            int group = LoadingProgress.Group((LoadingStage)i);
            Check(LoadingProgress.GroupStarts[group] <= i && i < LoadingProgress.GroupStarts[group + 1]);
        }
        Console.WriteLine($"PASS: {checks} loading model/tip contracts; no Unity operations.");
    }
}
