using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace FixWorld.Loading
{
    internal static class ModInitializationStage
    {
        private static readonly Type[] ModContentPackConstructorParameters =
        {
            typeof(DirectoryInfo),
            typeof(string),
            typeof(string),
            typeof(int),
            typeof(string),
            typeof(bool)
        };

        internal static ModInitializationResult Run()
        {
            ModInitializationInput input = CreateInput(out string fallbackReason);
            if (input == null)
            {
                Log.Warning(
                    "[FixWorld.Runtime] InitializeMods ownership is unavailable; " +
                    "using RimWorld before any stage mutation: " + fallbackReason);
                return RunVanillaFallback();
            }

            return ModBootStageRunner.Run(
                Descriptor(LoadingStageEventSource.FixWorld),
                operation => ExecuteOwned(input, operation));
        }

        private static ModInitializationInput CreateInput(
            out string fallbackReason)
        {
            List<ModContentPack> destination =
                LoadedModManager.RunningModsListForReading;
            if (destination == null)
            {
                fallbackReason = "RimWorld exposed no running-mod destination.";
                return null;
            }

            if (destination.Count != 0)
            {
                throw new InvalidOperationException(
                    "InitializeMods requires an empty running-mod destination, " +
                    "found " + destination.Count + " entries.");
            }

            if (typeof(ModContentPack).GetConstructor(
                    ModContentPackConstructorParameters) == null)
            {
                fallbackReason =
                    "The RimWorld ModContentPack constructor is incompatible.";
                return null;
            }

            List<ModMetaData> activeMods =
                ModsConfig.ActiveModsInLoadOrder.ToList();
            fallbackReason = null;
            return new ModInitializationInput(activeMods, destination);
        }

        private static ModInitializationResult ExecuteOwned(
            ModInitializationInput input,
            LoadingOperation stageOperation)
        {
            int disabled = 0;
            int nextLoadOrder = 0;
            for (int index = 0; index < input.ActiveMods.Count; index++)
            {
                ModMetaData metadata = input.ActiveMods[index];
                stageOperation.ReportProgress(
                    "Initializing " + metadata.Name + "   " +
                    (index + 1) + " / " + input.ActiveMods.Count,
                    force: index == 0 || index == input.ActiveMods.Count - 1);
                if (InitializeMod(
                        input.Destination,
                        metadata,
                        ref nextLoadOrder))
                {
                    continue;
                }

                disabled++;
            }

            return new ModInitializationResult(
                input.ActiveMods.Count,
                input.Destination.Count,
                disabled,
                usedVanillaFallback: false);
        }

        private static bool InitializeMod(
            List<ModContentPack> destination,
            ModMetaData metadata,
            ref int nextLoadOrder)
        {
            LoadingOperation operation = LoadingEvents.Begin(
                new LoadingStageEventDescriptor(
                    LoadingStage.Bootstrap,
                    LoadingStep.InitializeMod,
                    "Initialize mod",
                    metadata.Name,
                    LoadingModAttribution.Exact(
                        metadata.PackageId,
                        metadata.Name)));
            DeepProfiler.Start("Initializing " + metadata);
            try
            {
                if (!metadata.RootDir.Exists)
                {
                    ModsConfig.SetActive(metadata.PackageId, active: false);
                    Log.Warning(
                        "Failed to find active mod " + metadata.Name + "(" +
                        metadata.PackageIdPlayerFacing + ") at " +
                        metadata.RootDir);
                    operation.Fail();
                    return false;
                }

                ModContentPack content = new ModContentPack(
                    metadata.RootDir,
                    metadata.PackageId,
                    metadata.PackageIdPlayerFacing,
                    nextLoadOrder,
                    metadata.Name,
                    metadata.Official);
                nextLoadOrder++;
                destination.Add(content);
                GenTypes.ClearCache();
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("Error initializing mod: " + exception);
                ModsConfig.SetActive(metadata.PackageId, active: false);
                operation.Fail();
                return false;
            }
            finally
            {
                DeepProfiler.End();
                operation.Dispose();
            }
        }

        private static ModInitializationResult RunVanillaFallback()
        {
            int requested = ModsConfig.ActiveModsInLoadOrder.Count();
            int before = LoadedModManager.RunningModsListForReading.Count;
            return ModBootStageRunner.Run(
                Descriptor(LoadingStageEventSource.RimWorld),
                _ =>
                {
                    LoadedModManager.InitializeMods();
                    return new ModInitializationResult(
                        requested,
                        LoadedModManager.RunningModsListForReading.Count - before,
                        disabledCount: 0,
                        usedVanillaFallback: true);
                });
        }

        private static LoadingStageEventDescriptor Descriptor(
            LoadingStageEventSource source)
        {
            return new LoadingStageEventDescriptor(
                LoadingStage.Bootstrap,
                LoadingStep.InitializeMods,
                "Initialize active mods",
                "Creating ordered mod content packs",
                LoadingModAttribution.Global,
                source);
        }
    }

    internal sealed class ModInitializationInput
    {
        internal ModInitializationInput(
            IReadOnlyList<ModMetaData> activeMods,
            List<ModContentPack> destination)
        {
            ActiveMods = activeMods ??
                throw new ArgumentNullException(nameof(activeMods));
            Destination = destination ??
                throw new ArgumentNullException(nameof(destination));
        }

        internal IReadOnlyList<ModMetaData> ActiveMods { get; }

        internal List<ModContentPack> Destination { get; }
    }

    internal sealed class ModInitializationResult
    {
        internal ModInitializationResult(
            int requestedCount,
            int initializedCount,
            int disabledCount,
            bool usedVanillaFallback)
        {
            RequestedCount = requestedCount;
            InitializedCount = initializedCount;
            DisabledCount = disabledCount;
            UsedVanillaFallback = usedVanillaFallback;
        }

        internal int RequestedCount { get; }

        internal int InitializedCount { get; }

        internal int DisabledCount { get; }

        internal bool UsedVanillaFallback { get; }
    }
}
