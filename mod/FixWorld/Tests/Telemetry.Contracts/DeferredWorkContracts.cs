// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections;
using System.Collections.Generic;
using FixWorld.Patches;
using FixWorld.Telemetry;
using FixWorld.UI;
using Verse;

internal static class DeferredWorkContracts
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value)
        { if (!value) throw new Exception("Deferred work contract " + checks); checks++; }
        using var store = new TelemetryStore();
        using var loading = new LoadingProgress(store);
        LoadingHooks.Progress = loading;
        var queued = LongEventHandler.currentEvent;
        var work = LongEventHandler.toExecuteWhenFinished;
        Check(!DeferredWorkPump.TryBegin(null));
        Check(!DeferredWorkPump.TryBegin(queued));
        var order = new List<int>();
        var content = new ModContentPack();
        work.Add(() => { order.Add(1); work.Add(content.ReloadContent(() => order.Add(4))); });
        work.Add(content.ReloadContent(() => order.Add(2)));
        work.Add(() => { order.Add(3); throw new InvalidOperationException("fixture callback failure"); });
        Check(!DeferredWorkPump.TryBegin(queued));
        loading.Begin();
        Check(DeferredWorkPump.TryBegin(queued));
        Check(LongEventHandler.executingToExecuteWhenFinished && DeferredWorkPump.RequiresIsolatedLoadingFrame);
        Check(!DeferredWorkPump.TryBegin(queued));
        var iterator = queued.eventActionEnumerator;
        Check(iterator.MoveNext() && order.Count == 1 && order[0] == 1);
        Check(iterator.MoveNext() && order[1] == 2 && DeferredWorkPump.RequiresIsolatedLoadingFrame);
        Check(iterator.MoveNext() && order[2] == 3 && Log.Errors.Count == 1 && DeferredWorkPump.RequiresIsolatedLoadingFrame);
        Check(iterator.MoveNext() && order[3] == 4 && !DeferredWorkPump.RequiresIsolatedLoadingFrame);
        Check(!iterator.MoveNext() && work.Count == 0 && !LongEventHandler.executingToExecuteWhenFinished);
        Check(!loading.Current.Active && loading.Current.Stage == LoadingStage.Complete);
        Check(!iterator.MoveNext() && DeepProfiler.Depth == 0);
        work.Add(() => order.Add(5));
        Check(!DeferredWorkPump.TryBegin(queued)); // Unrelated later long events stay vanilla.
        work.Clear();
        LoadingHooks.Progress = null;
        Console.WriteLine($"PASS: {checks} production deferred-pump contracts; engine event boundary stubbed.");
    }
}

namespace FixWorld.Patches
{
    internal static class LoadingHooks { internal static LoadingProgress Progress { get; set; } }
}

namespace Verse
{
    internal static class LongEventHandler
    {
        internal sealed class Event { internal IEnumerator eventActionEnumerator = null; }
        internal static readonly Event currentEvent = new();
        internal static bool executingToExecuteWhenFinished = false;
        internal static readonly List<Action> toExecuteWhenFinished = new();
    }
    internal sealed class ModContentPack
    {
        internal Action ReloadContent(Action callback) => () => callback();
    }
    internal static class Log
    {
        internal static readonly List<string> Errors = new();
        internal static void Error(string message) => Errors.Add(message);
    }
    internal static class Prefs { internal static bool LogVerbose => true; }
    internal static class DeepProfiler
    {
        internal static bool enabled => true;
        internal static int Depth { get; private set; }
        internal static void Start(string name) => Depth++;
        internal static void End() => Depth--;
    }
}
