using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace FixWorld.Preloader
{
    internal static class PreloaderTimelineCapture
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<Assembly> ObservedAssemblies = new HashSet<Assembly>();

        private static bool started;
        private static bool assemblyCSharpObserved;
        private static int modAssemblyCount;
        private static int modAssembliesAtEntry;

        internal static void Start(long entryTimestamp)
        {
            lock (Sync)
            {
                if (started)
                {
                    return;
                }

                started = true;
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                PreloaderTimelineContract.PublishEntry(entryTimestamp, assemblies.Length);
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

                foreach (Assembly assembly in assemblies)
                {
                    TryObserveAssembly(assembly, entryTimestamp, true);
                }
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs arguments)
        {
            TryObserveAssembly(
                arguments.LoadedAssembly,
                System.Diagnostics.Stopwatch.GetTimestamp(),
                false);
        }

        private static void TryObserveAssembly(
            Assembly assembly,
            long timestamp,
            bool availableAtEntry)
        {
            try
            {
                ObserveAssembly(assembly, timestamp, availableAtEntry);
            }
            catch
            {
                // Early telemetry must never interfere with another assembly load.
            }
        }

        private static void ObserveAssembly(
            Assembly assembly,
            long timestamp,
            bool availableAtEntry)
        {
            lock (Sync)
            {
                if (assembly == null || !ObservedAssemblies.Add(assembly))
                {
                    return;
                }

                string assemblyName = assembly.GetName().Name;
                if (!assemblyCSharpObserved &&
                    string.Equals(assemblyName, "Assembly-CSharp", StringComparison.Ordinal))
                {
                    assemblyCSharpObserved = true;
                    PreloaderTimelineContract.PublishAssemblyCSharp(
                        timestamp,
                        availableAtEntry);
                }

                if (!IsModAssembly(assembly))
                {
                    return;
                }

                modAssemblyCount++;
                if (availableAtEntry)
                {
                    modAssembliesAtEntry++;
                }

                PreloaderTimelineContract.PublishModAssembly(
                    timestamp,
                    assemblyName,
                    modAssemblyCount,
                    modAssembliesAtEntry);
            }
        }

        private static bool IsModAssembly(Assembly assembly)
        {
            try
            {
                string location = assembly.Location;
                if (string.IsNullOrEmpty(location))
                {
                    return false;
                }

                string normalized = Path.GetFullPath(location).Replace('/', '\\');
                return normalized.IndexOf(
                           "\\Mods\\",
                           StringComparison.OrdinalIgnoreCase) >= 0 ||
                       normalized.IndexOf(
                           "\\workshop\\content\\294100\\",
                           StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
