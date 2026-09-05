// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using FixWorld.UI;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FixWorld.Patches
{
    // Installed once at early entry, not a second PatchAll at ModContentPack attachment.
    internal static class LoadingHooks
    {
        private const string Owner = "FixWorld.Loading";
        private static Harmony harmony;
        private static bool stagesInstalled;
        internal static LoadingProgress Progress { get; private set; }
        private static readonly Dictionary<MethodBase, LoadingStage> stages = new();
        internal static void Install(LoadingProgress progress)
        {
            if (harmony != null)
                return;
            Progress = progress;
            harmony = new Harmony(Owner);
            try
            {
                Patch(typeof(PlayDataLoader), "DoPlayLoad", nameof(Begin), finalizer: nameof(Failed));
                // Harmony may load while DoPlayLoad is already on the stack.
                Patch(typeof(LoadedModManager), "CreateModClasses", nameof(AttachStages));
                Patch(typeof(LongEventHandler), "LongEventsOnGUI", nameof(BeforeGui), nameof(AfterGui));
                Progress.Begin();
            }
            catch { Uninstall(); throw; }
        }
        private static void InstallStages()
        {
            if (stagesInstalled)
                return;
            try
            {
                Stage(typeof(LoadedModManager), "InitializeMods", LoadingStage.Mods);
                Stage(typeof(LoadedModManager), "LoadModContent", LoadingStage.Content);
                Stage(typeof(LoadedModManager), "LoadModXML", LoadingStage.Xml);
                Stage(typeof(LoadedModManager), "ParseAndProcessXML", LoadingStage.Import);
                Stage(typeof(DefGenerator), "GenerateImpliedDefs_PreResolve", LoadingStage.PreImplied);
                Stage(typeof(PlayDataLoader), "ResetStaticDataPre", LoadingStage.Resolve, postfix: true);
                Stage(typeof(DefGenerator), "GenerateImpliedDefs_PostResolve", LoadingStage.PostImplied);
                Stage(typeof(PlayDataLoader), "ResetStaticDataPost", LoadingStage.FinalizeDefs);
                Stage(typeof(KeyPrefs), "Init", LoadingStage.Runtime);
                Patch(typeof(DirectXmlCrossRefLoader), "ResolveAllWantedCrossReferences", nameof(CrossReferences));
                Patch(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished", nameof(BeginDeferred), finalizer: nameof(EndDeferred));
                harmony.Patch(Require(typeof(LongEventHandler), "UpdateCurrentAsynchronousEvent"), transpiler: Hook(nameof(Pump)));
                stagesInstalled = true;
            }
            catch { Uninstall(); throw; }
        }
        internal static void Uninstall()
        { harmony?.UnpatchAll(Owner); harmony = null; stagesInstalled = false; stages.Clear(); Progress = null; }
        private static MethodInfo Require(Type type, string name) => AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
        private static HarmonyMethod Hook(string name) => name == null ? null : new HarmonyMethod(Require(typeof(LoadingHooks), name));
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null)
            => harmony.Patch(Require(type, name), Hook(prefix), Hook(postfix), finalizer: Hook(finalizer));
        private static void Stage(Type type, string method, LoadingStage stage, bool postfix = false)
        {
            stages.Add(Require(type, method), stage);
            Patch(type, method, postfix ? null : nameof(Observe), postfix ? nameof(Observe) : null);
        }
        private static void Begin() { InstallStages(); Progress.Begin(); }
        private static void AttachStages() { InstallStages(); Progress.Transition(LoadingStage.Classes); }
        private static void Observe(MethodBase __originalMethod) => Progress.Transition(stages[__originalMethod]);
        private static Exception Failed(Exception __exception) { if (__exception != null) Progress.Fail(__exception); return __exception; }
        private static void CrossReferences(FailMode failReportMode) => Progress.CrossReferences(failReportMode == FailMode.Silent);
        private static void BeginDeferred(out bool __state) => __state = Progress.Transition(LoadingStage.Deferred);
        private static Exception EndDeferred(bool __state, Exception __exception)
        {
            if (__state)
            { if (__exception == null) Progress.Transition(LoadingStage.Complete); else Progress.Fail(__exception); }
            return __exception;
        }
        private static bool BeforeGui()
        {
            if (!DeferredWorkPump.RequiresIsolatedLoadingFrame)
                return true;
            // The original method also paints the backdrop. Keep it without the
            // content-dependent tip and mod-summary windows during content callbacks.
            UIMenuBackgroundManager.background ??= new UI_BackgroundMain();
            UIMenuBackgroundManager.background.BackgroundOnGUI();
            LoadingProgressUi.Draw(Progress);
            return false;
        }
        private static void AfterGui() { if (!DeferredWorkPump.RequiresIsolatedLoadingFrame) LoadingProgressUi.Draw(Progress); }
        private static IEnumerable<CodeInstruction> Pump(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var execute = Require(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished");
            var field = AccessTools.Field(typeof(LongEventHandler), "currentEvent") ?? throw new MissingFieldException("LongEventHandler.currentEvent");
            var rewritten = new List<CodeInstruction>(instructions);
            int index = rewritten.FindIndex(instruction => instruction.Calls(execute));
            if (index < 0)
                throw new InvalidOperationException("RimWorld deferred-work call site was not found.");
            Label originalLabel = generator.DefineLabel();
            var original = rewritten[index];
            var load = new CodeInstruction(OpCodes.Ldsfld, field);
            load.labels.AddRange(original.labels);
            original.labels.Clear();
            original.labels.Add(originalLabel);
            rewritten.InsertRange(index, new[] { load,
                new CodeInstruction(OpCodes.Call, Require(typeof(DeferredWorkPump), nameof(DeferredWorkPump.TryBegin))),
                new CodeInstruction(OpCodes.Brfalse, originalLabel), new CodeInstruction(OpCodes.Ret) });
            return rewritten;
        }
    }
}
