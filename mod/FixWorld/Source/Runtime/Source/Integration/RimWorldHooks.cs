using System;
using HarmonyLib;
using Verse;

namespace FixWorld.Integration
{
    internal static class RimWorldHooks
    {
        private const string OwnerPrefix = "smolblackhole.fixworld";
        private static readonly object Sync = new object();
        private static readonly HookGroup ModBootHookGroup = new HookGroup(
            "play-data and mod boot",
            OwnerPrefix + ".modboot",
            CombinePatchTypes(
                PlayDataHooks.PatchTypes,
                ModBootHooks.PatchTypes));
        private static readonly HookGroup LoadingHookGroup = new HookGroup(
            "loading",
            OwnerPrefix + ".loading",
            LoadingHooks.PatchTypes);
        private static readonly HookGroup LifecycleHookGroup = new HookGroup(
            "lifecycle",
            OwnerPrefix + ".lifecycle",
            LifecycleHooks.PatchTypes);
        private static readonly HookGroup DiagnosticHookGroup = new HookGroup(
            "diagnostics",
            OwnerPrefix + ".diagnostics",
            DiagnosticHooks.PatchTypes);

        internal static bool InstallModBoot()
        {
            lock (Sync)
            {
                return ModBootHookGroup.Install();
            }
        }

        internal static bool InstallRuntime(bool diagnosticsEnabled)
        {
            lock (Sync)
            {
                if (!LoadingHookGroup.Install())
                {
                    return false;
                }

                if (!LifecycleHookGroup.Install())
                {
                    LoadingHookGroup.Uninstall();
                    return false;
                }

                if (diagnosticsEnabled)
                {
                    DiagnosticHookGroup.Install();
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
                DiagnosticHookGroup.Uninstall();
                LifecycleHookGroup.Uninstall();
                LoadingHookGroup.Uninstall();
                ModBootHookGroup.Uninstall();
            }
        }

        private static Type[] CombinePatchTypes(Type[] first, Type[] second)
        {
            Type[] combined = new Type[first.Length + second.Length];
            Array.Copy(first, 0, combined, 0, first.Length);
            Array.Copy(second, 0, combined, first.Length, second.Length);
            return combined;
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
