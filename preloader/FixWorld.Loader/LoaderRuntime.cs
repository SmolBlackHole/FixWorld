using System;
using System.IO;
using System.Reflection;
using FixWorld.Preloader;
using FixWorld.RuntimeBridge;
using HarmonyLib;
using Verse;

namespace FixWorld.Loader
{
    public static class LoaderRuntime
    {
        private const string HarmonyOwner = "smolblackhole.fixworld.loader";
        private static readonly Guid SupportedAssemblyMvid =
            new Guid("61e41735-6189-4da4-9d21-0260257b5097");
        private static readonly object Sync = new object();

        private static bool started;

        public static void Start()
        {
            lock (Sync)
            {
                if (started)
                {
                    return;
                }

                MethodInfo target = ValidateContract();
                RuntimeContract runtime = LoadRuntime();
                runtime.StartEarly();
                MethodInfo prefix = typeof(ModLoadingPatch).GetMethod(
                    "Prefix",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null)
                {
                    throw new MissingMethodException(
                        typeof(ModLoadingPatch).FullName,
                        nameof(ModLoadingPatch.Prefix));
                }

                Harmony harmony = new Harmony(HarmonyOwner);
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                started = true;
                Log.Message(
                    "[FixWorld.Loader] FixWorld.Runtime is early-ready; " +
                    "LoadAllActiveMods is claimed.");
            }
        }

        private static RuntimeContract LoadRuntime()
        {
            string loaderDirectory = Path.GetDirectoryName(
                typeof(LoaderRuntime).Assembly.Location);
            if (string.IsNullOrEmpty(loaderDirectory))
            {
                throw new DirectoryNotFoundException(
                    "Could not locate the FixWorld loader directory.");
            }

            string runtimePath = Path.Combine(
                loaderDirectory,
                RuntimeContract.AssemblyName + ".dll");
            if (!File.Exists(runtimePath))
            {
                throw new FileNotFoundException(
                    "The FixWorld runtime is missing.",
                    runtimePath);
            }

            return RuntimeContract.Bind(Assembly.LoadFrom(runtimePath));
        }

        private static MethodInfo ValidateContract()
        {
            Assembly gameAssembly = typeof(LoadedModManager).Assembly;
            if (gameAssembly.ManifestModule.ModuleVersionId != SupportedAssemblyMvid)
            {
                throw new NotSupportedException(
                    "Unsupported Assembly-CSharp MVID " +
                    gameAssembly.ManifestModule.ModuleVersionId +
                    "; expected " + SupportedAssemblyMvid + ".");
            }

            MethodInfo target = typeof(LoadedModManager).GetMethod(
                nameof(LoadedModManager.LoadAllActiveMods),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);
            if (target == null || target.ReturnType != typeof(void))
            {
                throw new MissingMethodException(
                    typeof(LoadedModManager).FullName,
                    "LoadAllActiveMods(bool)");
            }

            return target;
        }

        private static class ModLoadingPatch
        {
            internal static bool Prefix(bool hotReload)
            {
                PreloaderTimelineContract.PublishLoaderOwnsModBoot();
                ModLoadingCoordinator.Run(hotReload);
                return false;
            }
        }
    }
}
