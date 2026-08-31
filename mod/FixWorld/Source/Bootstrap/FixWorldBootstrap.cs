using System;
using System.IO;
using System.Threading;
using FixWorld.Caching;
using FixWorld.Integration;
using FixWorld.Loading;
using HarmonyLib;

namespace FixWorld
{
    internal static class FixWorldBootstrap
    {
        private const string HarmonyId = "smolblackhole.fixworld";
        private static readonly object Sync = new object();
        private static bool initialized;

        internal static bool InitializeRuntime()
        {
            lock (Sync)
            {
                if (!initialized)
                {
                    TextureDdsCache.Initialize(FindModRoot());
                    LoadingSession.Start(true);
                    RimWorldHooks.InstallRuntime(new Harmony(HarmonyId));
                    Thread.MemoryBarrier();
                    initialized = true;
                }

                return string.Equals(
                    Environment.GetEnvironmentVariable("FIXWORLD_PRELOADER_ACTIVE"),
                    "1",
                    StringComparison.Ordinal);
            }
        }

        private static string FindModRoot()
        {
            string assemblyPath = typeof(FixWorldBootstrap).Assembly.Location;
            DirectoryInfo assemblyDirectory = new FileInfo(assemblyPath).Directory;
            DirectoryInfo modRoot = assemblyDirectory?.Parent;
            if (modRoot == null)
            {
                throw new DirectoryNotFoundException(
                    "FixWorld could not locate its mod directory.");
            }

            return modRoot.FullName;
        }
    }
}
