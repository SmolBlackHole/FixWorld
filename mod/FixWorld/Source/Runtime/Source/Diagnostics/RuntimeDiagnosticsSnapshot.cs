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
        internal const int CurrentSchemaVersion = 17;

        internal RuntimeDiagnosticsSnapshot(
            string completionSource,
            PreloaderTimelineSnapshot preloader,
            DdsReadAheadSnapshot ddsReadAhead,
            PlayDataTelemetrySnapshot loading,
            TextureProbeSnapshot textures,
            TextureDdsCacheSnapshot ddsCache,
            DeferredWorkSnapshot deferredWork,
            RuntimeSchedulerSnapshot scheduler,
            SystemMemorySnapshot memory,
            bool detailedCaptureEnabled)
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
            Textures = textures;
            DdsCache = ddsCache ??
                throw new ArgumentNullException(nameof(ddsCache));
            DeferredWork = deferredWork ??
                throw new ArgumentNullException(nameof(deferredWork));
            Scheduler = scheduler;
            Memory = memory;
            DetailedCaptureEnabled = detailedCaptureEnabled;
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

        [DataMember(Name = "textures", Order = 7)]
        internal TextureProbeSnapshot Textures { get; private set; }

        [DataMember(Name = "ddsCache", Order = 8)]
        internal TextureDdsCacheSnapshot DdsCache { get; private set; }

        [DataMember(Name = "deferred", Order = 9)]
        internal DeferredWorkSnapshot DeferredWork { get; private set; }

        [DataMember(Name = "scheduler", Order = 10)]
        internal RuntimeSchedulerSnapshot Scheduler { get; private set; }

        [DataMember(Name = "memory", Order = 11)]
        internal SystemMemorySnapshot Memory { get; private set; }

        [DataMember(Name = "detailedCaptureEnabled", Order = 12)]
        internal bool DetailedCaptureEnabled { get; private set; }
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
