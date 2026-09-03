using System;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class RimWorldHooks
    {
        private const string OwnerPrefix = "smolblackhole.fixworld";
        private static readonly object Sync = new object();
        private static readonly HookGroup BootstrapHookGroup = new HookGroup(
            "bootstrap",
            OwnerPrefix + ".bootstrap",
            BootstrapHooks.PatchTypes);
        private static readonly HookGroup PlayDataHookGroup = new HookGroup(
            "play-data",
            OwnerPrefix + ".playdata",
            PlayDataHooks.PatchTypes);
        private static readonly HookGroup TextureHookGroup = new HookGroup(
            "textures",
            OwnerPrefix + ".textures",
            TextureHooks.PatchTypes);
        private static readonly HookGroup LoadingUiHookGroup = new HookGroup(
            "loading-ui",
            OwnerPrefix + ".loading-ui",
            LoadingUiHooks.PatchTypes);
        private static readonly HookGroup LifecycleHookGroup = new HookGroup(
            "lifecycle",
            OwnerPrefix + ".lifecycle",
            LifecycleHooks.PatchTypes);
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
            private Harmony harmony;
            private bool installed;

            internal HookGroup(string name, string owner, Type[] patchTypes)
            {
                this.name = name;
                this.owner = owner;
                this.patchTypes = patchTypes;
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
                    Log.Error(
                        "[FixWorld] Could not install " + name +
                        " hooks: " + exception);
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
