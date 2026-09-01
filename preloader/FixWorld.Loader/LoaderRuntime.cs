using System;
using System.IO;
using System.Reflection;
using FixWorld.RuntimeBridge;
using Verse;

namespace FixWorld.Loader
{
    public static class LoaderRuntime
    {
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

                ValidateContract();
                RuntimeContract runtime = LoadRuntime();
                runtime.StartEarly();
                started = true;
                Log.Message(
                    "[FixWorld.Loader] FixWorld.Runtime accepted early control.");
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

        private static void ValidateContract()
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

        }
    }
}
