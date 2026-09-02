using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Serialization;

namespace FixWorld.Preloader
{
    [DataContract]
    internal struct PreloaderTimelineSnapshot
    {
        [DataMember(Name = "active", Order = 1)]
        internal bool Active { get; private set; }

        [DataMember(Name = "doorstopVersion", Order = 2)]
        internal string DoorstopVersion { get; private set; }

        [DataMember(Name = "assemblyCSharpObserved", Order = 3)]
        internal bool AssemblyCSharpObserved { get; private set; }

        [DataMember(Name = "assemblyCSharpAvailableAtEntry", Order = 4)]
        internal bool AssemblyCSharpAvailableAtEntry { get; private set; }

        [DataMember(Name = "assembliesAtEntry", Order = 5)]
        internal int AssembliesAtEntry { get; private set; }

        [DataMember(Name = "assembliesAtBootstrap", Order = 6)]
        internal int AssembliesAtBootstrap { get; private set; }

        [DataMember(Name = "modAssembliesAtEntry", Order = 7)]
        internal int ModAssembliesAtEntry { get; private set; }

        [DataMember(Name = "modAssembliesLoaded", Order = 8)]
        internal int ModAssembliesLoaded { get; private set; }

        [DataMember(Name = "firstModAssembly", Order = 9)]
        internal string FirstModAssembly { get; private set; }

        [DataMember(Name = "lastModAssembly", Order = 10)]
        internal string LastModAssembly { get; private set; }

        [DataMember(Name = "entryToAssemblyCSharpMs", Order = 11)]
        internal double? EntryToAssemblyCSharpMilliseconds { get; private set; }

        [DataMember(Name = "entryToFirstModAssemblyMs", Order = 12)]
        internal double? EntryToFirstModAssemblyMilliseconds { get; private set; }

        [DataMember(Name = "entryToLastModAssemblyMs", Order = 13)]
        internal double? EntryToLastModAssemblyMilliseconds { get; private set; }

        [DataMember(Name = "entryToBootstrapMs", Order = 14)]
        internal double? EntryToBootstrapMilliseconds { get; private set; }

        [DataMember(Name = "assemblyCSharpToFirstModAssemblyMs", Order = 15)]
        internal double? AssemblyCSharpToFirstModAssemblyMilliseconds
        {
            get;
            private set;
        }

        [DataMember(Name = "modAssemblyLoadMs", Order = 16)]
        internal double? ModAssemblyLoadMilliseconds { get; private set; }

        [DataMember(Name = "lastModAssemblyToBootstrapMs", Order = 17)]
        internal double? LastModAssemblyToBootstrapMilliseconds
        {
            get;
            private set;
        }

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

        internal const string RuntimeOwnsModBootVariable =
            "FIXWORLD_RUNTIME_OWNS_MOD_BOOT";

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

        internal static void PublishRuntimeOwnsModBoot()
        {
            Set(RuntimeOwnsModBootVariable, CurrentProcessId());
        }

        internal static bool RuntimeOwnsModBoot()
        {
            return string.Equals(
                Get(RuntimeOwnsModBootVariable),
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
