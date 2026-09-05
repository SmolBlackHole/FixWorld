// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using FixWorld.Telemetry;
using HarmonyLib;
using Verse;

namespace FixWorld.Patches
{
    internal static class ModLoadingHooks
    {
        private static Harmony harmony;
        private static ModLoadingTelemetry telemetry;
        internal static void Install(ModLoadingTelemetry value)
        {
            if (harmony != null)
                return;
            telemetry = value;
            harmony = new Harmony("FixWorld.ModLoading");
            try
            {
                Patch(AccessTools.Method(typeof(ModContentPack), "ReloadContent", new[] { typeof(bool) }), nameof(Assemblies));
                Patch(AccessTools.Method(typeof(ModContentPack), "ReloadContentInt"), nameof(Content));
                Patch(AccessTools.Method(typeof(DirectXmlLoader), "XmlAssetsInModFolder"), nameof(Xml));
                Patch(AccessTools.Constructor(typeof(LoadableXmlAsset), new[] { typeof(FileInfo), typeof(ModContentPack) }), nameof(XmlFile));
                harmony.Patch(AccessTools.Method(typeof(LoadedModManager), "CreateModClasses"), transpiler: Hook(nameof(Constructors)));
                harmony.Patch(AccessTools.Method(typeof(Log), "Error", new[] { typeof(string) }), prefix: Hook(nameof(Error)));
                harmony.Patch(AccessTools.Method(typeof(Log), "Warning", new[] { typeof(string) }), prefix: Hook(nameof(Warning)));
            }
            catch (Exception error)
            { Uninstall(); value.MarkUnavailable(error.Message); Bootstrap.BootEnvironment.Log("Mod loading observations unavailable: " + error); }
        }
        internal static void Uninstall() { harmony?.UnpatchAll("FixWorld.ModLoading"); harmony = null; telemetry = null; }
        private static HarmonyMethod Hook(string name) => new(AccessTools.Method(typeof(ModLoadingHooks), name));
        private static void Patch(MethodBase method, string prefix)
        {
            if (method == null)
                throw new MissingMethodException("Mod loading observation point missing");
            harmony.Patch(method, prefix: Hook(prefix), finalizer: Hook(nameof(End)));
        }
        private static ModLoadingTelemetry.Scope Begin(ModContentPack mod, ModLoadPart? part)
            => telemetry?.Begin(mod?.PackageId, mod?.Name, part);
        private static void Assemblies(ModContentPack __instance, out ModLoadingTelemetry.Scope __state) => __state = Begin(__instance, ModLoadPart.Assemblies);
        private static void Content(ModContentPack __instance, out ModLoadingTelemetry.Scope __state) => __state = Begin(__instance, ModLoadPart.Content);
        private static void Xml(ModContentPack mod, out ModLoadingTelemetry.Scope __state) => __state = Begin(mod, ModLoadPart.Xml);
        // XML parsing runs on multiple threads. The constructor carries the real owner.
        private static void XmlFile(ModContentPack mod, out ModLoadingTelemetry.Scope __state) => __state = Begin(mod, null);
        private static Exception End(ModLoadingTelemetry.Scope __state, Exception __exception)
        {
            if (__exception != null)
                __state?.Fail(__exception);
            __state?.Dispose();
            return __exception;
        }
        private static void Error(string text) { if (LoadingHooks.Progress?.Current?.Active == true) telemetry?.RecordMessage(text, true); }
        private static void Warning(string text) { if (LoadingHooks.Progress?.Current?.Active == true) telemetry?.RecordMessage(text, false); }
        private static object Construct(Type type, object[] args)
        {
            using (var scope = Begin(args.Length > 0 ? args[0] as ModContentPack : null, ModLoadPart.Constructors))
            {
                try
                { return Activator.CreateInstance(type, args); }
                catch (Exception error) { scope?.Fail(error); throw; }
            }
        }
        private static IEnumerable<CodeInstruction> Constructors(IEnumerable<CodeInstruction> instructions)
        {
            var target = AccessTools.Method(typeof(Activator), "CreateInstance", new[] { typeof(Type), typeof(object[]) });
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(target))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ModLoadingHooks), nameof(Construct)); replaced++; }
                yield return instruction;
            }
            if (replaced != 1)
                throw new InvalidOperationException("Expected one mod constructor call.");
        }
    }
}
