using System;
using System.IO;
using System.Reflection;

namespace FixWorld.Preloader
{
    internal static class EarlyLoaderBridge
    {
        private static readonly object Sync = new object();

        private static PreloaderLog log;
        private static string toolsRoot;
        private static bool loaderStarted;
        private static bool started;

        internal static void Start(PreloaderLog preloaderLog)
        {
            lock (Sync)
            {
                if (started)
                {
                    return;
                }

                started = true;
                log = preloaderLog;
                toolsRoot = Path.GetDirectoryName(
                    typeof(EarlyLoaderBridge).Assembly.Location);
                if (string.IsNullOrEmpty(toolsRoot))
                {
                    throw new DirectoryNotFoundException(
                        "Could not locate the FixWorld early-loader directory.");
                }

                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (TryStartLoader(assembly))
                    {
                        break;
                    }
                }
            }
        }

        private static void OnAssemblyLoad(
            object sender,
            AssemblyLoadEventArgs arguments)
        {
            TryStartLoader(arguments.LoadedAssembly);
        }

        private static bool TryStartLoader(Assembly assembly)
        {
            if (!string.Equals(
                    assembly?.GetName().Name,
                    "Assembly-CSharp",
                    StringComparison.Ordinal))
            {
                return false;
            }

            lock (Sync)
            {
                if (loaderStarted)
                {
                    return true;
                }

                try
                {
                    string loaderPath = Path.Combine(
                        toolsRoot,
                        "FixWorld.Loader.dll");
                    if (!File.Exists(loaderPath))
                    {
                        throw new FileNotFoundException(
                            "The FixWorld loader is missing.",
                            loaderPath);
                    }

                    InstalledHarmonyResolver.Load(
                        PreloaderPaths.FindGameRoot(),
                        log);
                    Assembly loader = Assembly.LoadFrom(loaderPath);
                    Type entrypoint = loader.GetType(
                        "FixWorld.Loader.LoaderRuntime",
                        throwOnError: true);
                    MethodInfo start = entrypoint.GetMethod(
                        "Start",
                        BindingFlags.Public | BindingFlags.Static);
                    if (start == null || start.GetParameters().Length != 0)
                    {
                        throw new MissingMethodException(
                            entrypoint.FullName,
                            "Start()");
                    }

                    start.Invoke(null, null);
                    loaderStarted = true;
                    AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                    log.Write(
                        "FixWorld.Loader started FixWorld.Runtime.");
                    return true;
                }
                catch (Exception exception)
                {
                    AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                    log.Write("Could not start FixWorld.Loader: " + Unwrap(exception));
                    return true;
                }
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation &&
                   invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }
    }
}
