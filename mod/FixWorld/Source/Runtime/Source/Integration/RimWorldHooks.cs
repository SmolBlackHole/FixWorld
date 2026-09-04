using System;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class RimWorldHooks
    {
        private const string OwnerPrefix = "smolblackhole.fixworld";
        private static readonly object Sync = new();
        private static readonly HookGroup BootstrapHookGroup = new(
            "bootstrap",
            OwnerPrefix + ".bootstrap",
            BootstrapHooks.PatchTypes);
        private static readonly HookGroup PlayDataHookGroup = new(
            "play-data",
            OwnerPrefix + ".playdata",
            PlayDataHooks.PatchTypes);
        private static readonly HookGroup TextureHookGroup = new(
            "textures",
            OwnerPrefix + ".textures",
            TextureHooks.PatchTypes);
        private static readonly HookGroup LoadingUiHookGroup = new(
            "loading-ui",
            OwnerPrefix + ".loading-ui",
            LoadingUiHooks.PatchTypes);
        private static readonly HookGroup LifecycleHookGroup = new(
            "lifecycle",
            OwnerPrefix + ".lifecycle",
            LifecycleHooks.PatchTypes);
        private static readonly HookGroup RuntimeProfilingHookGroup =
            new(
                "runtime profiling",
                OwnerPrefix + ".profiling",
                RuntimeProfilingHooks.PatchTypes,
                required: false);
        private static readonly HookGroup PathfindingOptimizationHookGroup =
            new(
                "pathfinding optimization",
                OwnerPrefix + ".pathfinding",
                PathfindingOptimizationHooks.PatchTypes,
                required: false);
        internal static bool InstallBootstrap()
        {
            lock (Sync)
            {
                return BootstrapHookGroup.Install();
            }
        }

        internal static bool InstallRuntime()
        {
            lock (Sync)
            {
                if (!PlayDataHookGroup.Install())
                {
                    return false;
                }

                if (!TextureHookGroup.Install())
                {
                    PlayDataHookGroup.Uninstall();
                    return false;
                }

                if (!LoadingUiHookGroup.Install())
                {
                    TextureHookGroup.Uninstall();
                    PlayDataHookGroup.Uninstall();
                    return false;
                }

                if (!LifecycleHookGroup.Install())
                {
                    LoadingUiHookGroup.Uninstall();
                    TextureHookGroup.Uninstall();
                    PlayDataHookGroup.Uninstall();
                    return false;
                }

                if (PathfindingOptimizationHookGroup.Install())
                {
                    Log.Message(
                        "[FixWorld] Connectivity union deduplication active.");
                }

                RuntimeProfilingHookGroup.Install();
                return true;
            }
        }

        internal static bool IsFixWorldOwner(string owner)
        {
            return string.Equals(owner, OwnerPrefix, StringComparison.Ordinal) ||
                   owner?.StartsWith(
                       OwnerPrefix + ".",
                       StringComparison.Ordinal) == true;
        }

        internal static void Uninstall()
        {
            lock (Sync)
            {
                RuntimeProfilingHookGroup.Uninstall();
                PathfindingOptimizationHookGroup.Uninstall();
                LifecycleHookGroup.Uninstall();
                LoadingUiHookGroup.Uninstall();
                TextureHookGroup.Uninstall();
                PlayDataHookGroup.Uninstall();
                BootstrapHookGroup.Uninstall();
            }
        }

        private sealed class HookGroup
        {
            private readonly string name;
            private readonly string owner;
            private readonly Type[] patchTypes;
            private readonly bool required;
            private Harmony harmony;
            private bool installed;

            internal HookGroup(
                string name,
                string owner,
                Type[] patchTypes,
                bool required = true)
            {
                this.name = name;
                this.owner = owner;
                this.patchTypes = patchTypes;
                this.required = required;
            }

            internal bool Install()
            {
                if (installed)
                {
                    return true;
                }

                harmony = harmony ?? new Harmony(owner);
                try
                {
                    foreach (Type patchType in patchTypes)
                    {
                        harmony.CreateClassProcessor(patchType).Patch();
                    }

                    installed = true;
                    return true;
                }
                catch (Exception exception)
                {
                    string message =
                        "[FixWorld] Could not install " + name +
                        " hooks: " + exception;
                    if (required)
                    {
                        Log.Error(message);
                    }
                    else
                    {
                        Log.Warning(message);
                    }

                    Uninstall();
                    return false;
                }
            }

            internal void Uninstall()
            {
                try
                {
                    harmony?.UnpatchAll(owner);
                }
                catch (Exception exception)
                {
                    Log.Error(
                        "[FixWorld] Could not roll back " + name +
                        " hooks: " + exception);
                }

                installed = false;
            }
        }
    }
}
