// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FixWorld.Bootstrap;
using FixWorld.Textures;
using HarmonyLib;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FixWorld.Patches
{
    internal static class TextureHooks
    {
        private const string Owner = "FixWorld.Dds";
        private static Harmony harmony;
        private static TextureDdsCache cache;
        private static bool prepared;

        internal static void Install(TextureDdsCache value)
        {
            if (harmony != null)
                return;
            cache = value;
            harmony = new Harmony(Owner);
            try
            {
                Patch(typeof(LoadedModManager), "LoadModContent", nameof(Prepare));
                // Early entry can happen after LoadModContent has queued Unity callbacks.
                Patch(typeof(LoadedModManager), "CreateModClasses", nameof(Prepare));
                Patch(typeof(ModContentPack), "GetAllFilesForMod", postfix: nameof(Discovered));
                Patch(typeof(ModContentLoader<Texture2D>), "LoadTexture", nameof(Load));
            }
            catch (Exception error)
            {
                Uninstall();
                BootEnvironment.Log("DDS hooks unavailable; using RimWorld textures: " + error);
            }
        }
        internal static void Uninstall()
        { harmony?.UnpatchAll(Owner); harmony = null; cache = null; prepared = false; }
        private static void Patch(Type type, string method, string prefix = null, string postfix = null)
        {
            var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
            harmony.Patch(target,
                prefix == null ? null : new HarmonyMethod(typeof(TextureHooks), prefix) { priority = Priority.Last },
                postfix == null ? null : new HarmonyMethod(typeof(TextureHooks), postfix));
        }
        private static void Prepare()
        {
            if (prepared)
                return;
            var mod = LoadedModManager.RunningModsListForReading.FirstOrDefault(item =>
                string.Equals(item.PackageId, BootEnvironment.PackageId, StringComparison.OrdinalIgnoreCase));
            if (mod == null)
                return;
            prepared = true;
            try
            {
                cache.Attach(mod.RootDir);
                cache.Prepare();
                cache.BeginTextureDiscovery();
                // Add after LoadModContent has queued all mod texture callbacks,
                // not in its prefix (which would run before those callbacks).
            }
            catch (Exception error) { Log.Warning("[FixWorld] DDS discovery unavailable: " + error); }
        }
        private static void Discovered(ModContentPack mod, string contentPath,
            Func<string, bool> validateExtension, List<string> foldersToLoadDebug,
            Dictionary<string, FileInfo> __result)
        {
            try
            { cache.ObserveTextureFiles(mod, contentPath, validateExtension, foldersToLoadDebug, __result); }
            catch (Exception error) { Log.Warning("[FixWorld] DDS discovery fell back for " + mod?.PackageId + ": " + error.Message); }
        }
        private static bool Load(VirtualFile file, ref Texture2D __result)
        {
            if (!Prefs.TextureCompression || !SystemInfo.SupportsTextureFormat(TextureFormat.BC7))
                return true;
            if (!cache.TryLoad(file, out var texture))
                return true;
            __result = texture;
            return false;
        }
    }
}
