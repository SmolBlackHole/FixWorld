using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace FixWorld.Integration
{
    [Flags]
    internal enum HarmonyPatchKinds
    {
        Prefix = 1,
        Postfix = 2,
        Transpiler = 4,
        Finalizer = 8,
        All = Prefix | Postfix | Transpiler | Finalizer
    }

    internal static class HarmonyPatchInspector
    {
        internal static bool Any(
            MethodBase method,
            HarmonyPatchKinds kinds = HarmonyPatchKinds.All,
            Func<Patch, bool> predicate = null)
        {
            return Enumerate(method, kinds).Any(
                patch => predicate == null || predicate(patch));
        }

        internal static string GetOwners(
            MethodBase method,
            HarmonyPatchKinds kinds = HarmonyPatchKinds.All,
            Func<Patch, bool> predicate = null)
        {
            string[] owners = Enumerate(method, kinds)
                .Where(patch => predicate == null || predicate(patch))
                .Select(patch => patch.owner)
                .Where(owner => !string.IsNullOrWhiteSpace(owner))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(owner => owner, StringComparer.Ordinal)
                .ToArray();
            return owners.Length == 0 ? null : string.Join(",", owners);
        }

        internal static void CollectOwners(
            MethodBase method,
            ISet<string> owners,
            HarmonyPatchKinds kinds = HarmonyPatchKinds.All,
            Func<Patch, bool> predicate = null)
        {
            if (owners == null)
            {
                throw new ArgumentNullException(nameof(owners));
            }

            foreach (Patch patch in Enumerate(method, kinds))
            {
                if ((predicate == null || predicate(patch)) &&
                    !string.IsNullOrWhiteSpace(patch.owner))
                {
                    owners.Add(patch.owner);
                }
            }
        }

        private static IEnumerable<Patch> Enumerate(
            MethodBase method,
            HarmonyPatchKinds kinds)
        {
            Patches patches = method == null ? null : Harmony.GetPatchInfo(method);
            if (patches == null)
            {
                yield break;
            }

            if ((kinds & HarmonyPatchKinds.Prefix) != 0)
            {
                foreach (Patch patch in patches.Prefixes)
                {
                    yield return patch;
                }
            }

            if ((kinds & HarmonyPatchKinds.Postfix) != 0)
            {
                foreach (Patch patch in patches.Postfixes)
                {
                    yield return patch;
                }
            }

            if ((kinds & HarmonyPatchKinds.Transpiler) != 0)
            {
                foreach (Patch patch in patches.Transpilers)
                {
                    yield return patch;
                }
            }

            if ((kinds & HarmonyPatchKinds.Finalizer) != 0)
            {
                foreach (Patch patch in patches.Finalizers)
                {
                    yield return patch;
                }
            }
        }
    }
}
