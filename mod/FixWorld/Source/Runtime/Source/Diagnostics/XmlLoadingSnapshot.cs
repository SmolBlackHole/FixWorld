using System;
using System.Collections.Generic;

namespace FixWorld.Diagnostics
{
    internal sealed class XmlLoadingSnapshot
    {
        internal static readonly XmlLoadingSnapshot Empty =
            new XmlLoadingSnapshot(
                false,
                false,
                0,
                0,
                0,
                0L,
                0,
                0,
                0.0,
                "replaced-by-play-data-pipeline",
                Array.Empty<XmlModLoadingSnapshot>());

        internal XmlLoadingSnapshot(
            bool owned,
            bool hotReload,
            int workerCount,
            int mods,
            int files,
            long bytes,
            int fallbackMods,
            int failedMods,
            double wallMilliseconds,
            string fallbackReason,
            IReadOnlyList<XmlModLoadingSnapshot> modDetails)
        {
            Owned = owned;
            HotReload = hotReload;
            WorkerCount = workerCount;
            Mods = mods;
            Files = files;
            Bytes = bytes;
            FallbackMods = fallbackMods;
            FailedMods = failedMods;
            WallMilliseconds = wallMilliseconds;
            FallbackReason = fallbackReason;
            ModDetails = modDetails;
        }

        internal bool Owned { get; }
        internal bool HotReload { get; }
        internal int WorkerCount { get; }
        internal int Mods { get; }
        internal int Files { get; }
        internal long Bytes { get; }
        internal int FallbackMods { get; }
        internal int FailedMods { get; }
        internal double WallMilliseconds { get; }
        internal string FallbackReason { get; }
        internal IReadOnlyList<XmlModLoadingSnapshot> ModDetails { get; }
    }

    internal readonly struct XmlModLoadingSnapshot
    {
        internal XmlModLoadingSnapshot(
            string packageId,
            string modName,
            int files,
            long bytes,
            double discoveryMilliseconds,
            double parseMilliseconds,
            double waitMilliseconds,
            bool fallback,
            bool failed)
        {
            PackageId = packageId;
            ModName = modName;
            Files = files;
            Bytes = bytes;
            DiscoveryMilliseconds = discoveryMilliseconds;
            ParseMilliseconds = parseMilliseconds;
            WaitMilliseconds = waitMilliseconds;
            Fallback = fallback;
            Failed = failed;
        }

        internal string PackageId { get; }
        internal string ModName { get; }
        internal int Files { get; }
        internal long Bytes { get; }
        internal double DiscoveryMilliseconds { get; }
        internal double ParseMilliseconds { get; }
        internal double WaitMilliseconds { get; }
        internal bool Fallback { get; }
        internal bool Failed { get; }
    }
}
