using System;
using System.Globalization;
using System.Runtime.Serialization;
using FixWorld.PlayData;
using FixWorld.Preloader;
using FixWorld.Textures;

namespace FixWorld.Diagnostics
{
    [DataContract]
    internal sealed class RuntimeDiagnosticsSnapshot
    {
        internal const int CurrentSchemaVersion = 19;

        internal RuntimeDiagnosticsSnapshot(
            string completionSource,
            PreloaderTimelineSnapshot preloader,
            DdsReadAheadSnapshot ddsReadAhead,
            PlayDataTelemetrySnapshot loading,
            TextureDdsCacheSnapshot ddsCache,
            RuntimeSchedulerSnapshot scheduler,
            SystemMemorySnapshot memory)
        {
            SchemaVersion = CurrentSchemaVersion;
            CompletedUtc = DateTime.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture);
            CompletionSource = string.IsNullOrWhiteSpace(completionSource)
                ? "unknown"
                : completionSource;
            Preloader = preloader;
            DdsReadAhead = ddsReadAhead;
            Loading = loading ?? throw new ArgumentNullException(nameof(loading));
            DdsCache = ddsCache ??
                throw new ArgumentNullException(nameof(ddsCache));
            Scheduler = scheduler;
            Memory = memory;
        }

        [DataMember(Name = "schemaVersion", Order = 1)]
        internal int SchemaVersion { get; private set; }

        [DataMember(Name = "completedUtc", Order = 2)]
        internal string CompletedUtc { get; private set; }

        [DataMember(Name = "completionSource", Order = 3)]
        internal string CompletionSource { get; private set; }

        [DataMember(Name = "preloader", Order = 4)]
        internal PreloaderTimelineSnapshot Preloader { get; private set; }

        [DataMember(Name = "ddsReadAhead", Order = 5)]
        internal DdsReadAheadSnapshot DdsReadAhead { get; private set; }

        [DataMember(Name = "loader", Order = 6)]
        internal PlayDataTelemetrySnapshot Loading { get; private set; }

        [DataMember(Name = "ddsCache", Order = 7)]
        internal TextureDdsCacheSnapshot DdsCache { get; private set; }

        [DataMember(Name = "scheduler", Order = 8)]
        internal RuntimeSchedulerSnapshot Scheduler { get; private set; }

        [DataMember(Name = "memory", Order = 9)]
        internal SystemMemorySnapshot Memory { get; private set; }
    }

    [DataContract]
    internal struct RuntimeSchedulerSnapshot
    {
        internal RuntimeSchedulerSnapshot(
            int workerCount,
            int pendingMainThreadActions)
        {
            WorkerCount = workerCount;
            PendingMainThreadActions = pendingMainThreadActions;
        }

        [DataMember(Name = "workerCount", Order = 1)]
        internal int WorkerCount { get; private set; }

        [DataMember(Name = "pendingMainThreadActions", Order = 2)]
        internal int PendingMainThreadActions { get; private set; }
    }
}
