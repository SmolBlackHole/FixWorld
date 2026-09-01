using System;
using System.Diagnostics;
using System.Globalization;

namespace FixWorld.Preloader
{
    internal readonly struct PreloaderTimelineSnapshot
    {
        internal bool Active { get; }
        internal string DoorstopVersion { get; }
        internal bool AssemblyCSharpObserved { get; }
        internal bool AssemblyCSharpAvailableAtEntry { get; }
        internal int AssembliesAtEntry { get; }
        internal int AssembliesAtBootstrap { get; }
        internal int ModAssembliesAtEntry { get; }
        internal int ModAssembliesLoaded { get; }
        internal string FirstModAssembly { get; }
        internal string LastModAssembly { get; }
        internal double? EntryToAssemblyCSharpMilliseconds { get; }
        internal double? EntryToFirstModAssemblyMilliseconds { get; }
        internal double? EntryToLastModAssemblyMilliseconds { get; }
        internal double? EntryToBootstrapMilliseconds { get; }
        internal double? AssemblyCSharpToFirstModAssemblyMilliseconds { get; }
        internal double? ModAssemblyLoadMilliseconds { get; }
        internal double? LastModAssemblyToBootstrapMilliseconds { get; }

        internal PreloaderTimelineSnapshot(
            bool active,
            string doorstopVersion,
            bool assemblyCSharpObserved,
            bool assemblyCSharpAvailableAtEntry,
            int assembliesAtEntry,
            int assembliesAtBootstrap,
            int modAssembliesAtEntry,
            int modAssembliesLoaded,
            string firstModAssembly,
            string lastModAssembly,
            double? entryToAssemblyCSharpMilliseconds,
            double? entryToFirstModAssemblyMilliseconds,
            double? entryToLastModAssemblyMilliseconds,
            double? entryToBootstrapMilliseconds,
            double? assemblyCSharpToFirstModAssemblyMilliseconds,
            double? modAssemblyLoadMilliseconds,
            double? lastModAssemblyToBootstrapMilliseconds)
        {
            Active = active;
            DoorstopVersion = doorstopVersion;
            AssemblyCSharpObserved = assemblyCSharpObserved;
            AssemblyCSharpAvailableAtEntry = assemblyCSharpAvailableAtEntry;
            AssembliesAtEntry = assembliesAtEntry;
            AssembliesAtBootstrap = assembliesAtBootstrap;
            ModAssembliesAtEntry = modAssembliesAtEntry;
            ModAssembliesLoaded = modAssembliesLoaded;
            FirstModAssembly = firstModAssembly;
            LastModAssembly = lastModAssembly;
            EntryToAssemblyCSharpMilliseconds = entryToAssemblyCSharpMilliseconds;
            EntryToFirstModAssemblyMilliseconds = entryToFirstModAssemblyMilliseconds;
            EntryToLastModAssemblyMilliseconds = entryToLastModAssemblyMilliseconds;
            EntryToBootstrapMilliseconds = entryToBootstrapMilliseconds;
            AssemblyCSharpToFirstModAssemblyMilliseconds =
                assemblyCSharpToFirstModAssemblyMilliseconds;
            ModAssemblyLoadMilliseconds = modAssemblyLoadMilliseconds;
            LastModAssemblyToBootstrapMilliseconds =
                lastModAssemblyToBootstrapMilliseconds;
        }
    }

    internal static class PreloaderTimelineContract
    {
        internal const string ActiveVariable = "FIXWORLD_PRELOADER_ACTIVE";
        internal const string DoorstopVersion = "4.4.0";

        internal const string LoaderOwnsModBootVariable =
            "FIXWORLD_LOADER_OWNS_MOD_BOOT";

        private const string EntryTicksVariable = "FIXWORLD_PRELOADER_ENTRY_TICKS";
        private const string AssemblyCSharpTicksVariable =
            "FIXWORLD_PRELOADER_ASSEMBLY_CSHARP_TICKS";
        private const string AssemblyCSharpAtEntryVariable =
            "FIXWORLD_PRELOADER_ASSEMBLY_CSHARP_AT_ENTRY";
        private const string AssembliesAtEntryVariable =
            "FIXWORLD_PRELOADER_ASSEMBLIES_AT_ENTRY";
        private const string ModAssembliesAtEntryVariable =
            "FIXWORLD_PRELOADER_MOD_ASSEMBLIES_AT_ENTRY";
        private const string ModAssemblyCountVariable =
            "FIXWORLD_PRELOADER_MOD_ASSEMBLY_COUNT";
        private const string FirstModAssemblyTicksVariable =
            "FIXWORLD_PRELOADER_FIRST_MOD_ASSEMBLY_TICKS";
        private const string FirstModAssemblyNameVariable =
            "FIXWORLD_PRELOADER_FIRST_MOD_ASSEMBLY_NAME";
        private const string LastModAssemblyTicksVariable =
            "FIXWORLD_PRELOADER_LAST_MOD_ASSEMBLY_TICKS";
        private const string LastModAssemblyNameVariable =
            "FIXWORLD_PRELOADER_LAST_MOD_ASSEMBLY_NAME";

        internal static void PublishEntry(long timestamp, int assemblyCount)
        {
            Set(ActiveVariable, CurrentProcessId());
            Set(EntryTicksVariable, timestamp);
            Set(AssembliesAtEntryVariable, assemblyCount);
        }

        internal static void PublishLoaderOwnsModBoot()
        {
            Set(LoaderOwnsModBootVariable, CurrentProcessId());
        }

        internal static bool LoaderOwnsModBoot()
        {
            return string.Equals(
                Get(LoaderOwnsModBootVariable),
                CurrentProcessId(),
                StringComparison.Ordinal);
        }

        internal static void PublishAssemblyCSharp(long timestamp, bool availableAtEntry)
        {
            Set(AssemblyCSharpTicksVariable, timestamp);
            Set(AssemblyCSharpAtEntryVariable, availableAtEntry ? "1" : "0");
        }

        internal static void PublishModAssembly(
            long timestamp,
            string assemblyName,
            int count,
            int countAtEntry)
        {
            if (count == 1)
            {
                Set(FirstModAssemblyTicksVariable, timestamp);
                Set(FirstModAssemblyNameVariable, assemblyName);
            }

            Set(LastModAssemblyTicksVariable, timestamp);
            Set(LastModAssemblyNameVariable, assemblyName);
            Set(ModAssemblyCountVariable, count);
            Set(ModAssembliesAtEntryVariable, countAtEntry);
        }

        internal static PreloaderTimelineSnapshot CaptureAtBootstrap(
            long bootstrapTimestamp,
            int assemblyCount)
        {
            bool active = string.Equals(
                Get(ActiveVariable),
                CurrentProcessId(),
                StringComparison.Ordinal);
            long entryTimestamp = GetLong(EntryTicksVariable);
            long assemblyCSharpTimestamp = GetLong(AssemblyCSharpTicksVariable);
            long firstModAssemblyTimestamp = GetLong(FirstModAssemblyTicksVariable);
            long lastModAssemblyTimestamp = GetLong(LastModAssemblyTicksVariable);

            return new PreloaderTimelineSnapshot(
                active,
                active ? DoorstopVersion : null,
                assemblyCSharpTimestamp > 0L,
                string.Equals(
                    Get(AssemblyCSharpAtEntryVariable),
                    "1",
                    StringComparison.Ordinal),
                GetInt(AssembliesAtEntryVariable),
                assemblyCount,
                GetInt(ModAssembliesAtEntryVariable),
                GetInt(ModAssemblyCountVariable),
                Get(FirstModAssemblyNameVariable),
                Get(LastModAssemblyNameVariable),
                MillisecondsBetween(entryTimestamp, assemblyCSharpTimestamp),
                MillisecondsBetween(entryTimestamp, firstModAssemblyTimestamp),
                MillisecondsBetween(entryTimestamp, lastModAssemblyTimestamp),
                MillisecondsBetween(entryTimestamp, bootstrapTimestamp),
                MillisecondsBetween(assemblyCSharpTimestamp, firstModAssemblyTimestamp),
                MillisecondsBetween(firstModAssemblyTimestamp, lastModAssemblyTimestamp),
                MillisecondsBetween(lastModAssemblyTimestamp, bootstrapTimestamp));
        }

        private static double? MillisecondsBetween(long start, long end)
        {
            return start > 0L && end >= start
                ? (end - start) * 1000.0 / Stopwatch.Frequency
                : (double?)null;
        }

        private static int GetInt(string name)
        {
            return int.TryParse(
                Get(name),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : 0;
        }

        private static long GetLong(string name)
        {
            return long.TryParse(
                Get(name),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long value)
                ? value
                : 0L;
        }

        private static string Get(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        private static string CurrentProcessId()
        {
            return Process.GetCurrentProcess().Id.ToString(
                CultureInfo.InvariantCulture);
        }

        private static void Set(string name, int value)
        {
            Set(name, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Set(string name, long value)
        {
            Set(name, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Set(string name, string value)
        {
            Environment.SetEnvironmentVariable(
                name,
                value,
                EnvironmentVariableTarget.Process);
        }
    }
}
