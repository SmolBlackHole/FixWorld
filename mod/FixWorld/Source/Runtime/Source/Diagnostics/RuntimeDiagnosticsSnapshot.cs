using System;
using FixWorld.Loading;
using FixWorld.PlayData;
using FixWorld.Preloader;
using FixWorld.Textures;

namespace FixWorld.Diagnostics
{
    internal sealed class RuntimeDiagnosticsSnapshot
    {
        internal const int CurrentSchemaVersion = 1;

        internal RuntimeDiagnosticsSnapshot(
            string completionSource,
            PreloaderTimelineSnapshot preloader,
            LoadingMeasurement loading,
            TextureProbeSnapshot textures,
            TextureDdsCacheSnapshot ddsCache,
            DeferredWorkSnapshot deferredWork,
            RuntimeSchedulerSnapshot scheduler,
            SystemMemorySnapshot memory,
            bool detailedCaptureEnabled)
        {
            SchemaVersion = CurrentSchemaVersion;
            CapturedUtc = DateTime.UtcNow;
            CompletionSource = string.IsNullOrWhiteSpace(completionSource)
                ? "unknown"
                : completionSource;
            Preloader = preloader;
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

        internal int SchemaVersion { get; }

        internal DateTime CapturedUtc { get; }

        internal string CompletionSource { get; }

        internal PreloaderTimelineSnapshot Preloader { get; }

        internal LoadingMeasurement Loading { get; }

        internal TextureProbeSnapshot Textures { get; }

        internal TextureDdsCacheSnapshot DdsCache { get; }

        internal DeferredWorkSnapshot DeferredWork { get; }

        internal RuntimeSchedulerSnapshot Scheduler { get; }

        internal SystemMemorySnapshot Memory { get; }

        internal bool DetailedCaptureEnabled { get; }
    }

    internal readonly struct RuntimeSchedulerSnapshot
    {
        internal RuntimeSchedulerSnapshot(
            int workerCount,
            int pendingMainThreadActions)
        {
            WorkerCount = workerCount;
            PendingMainThreadActions = pendingMainThreadActions;
        }

        internal int WorkerCount { get; }

        internal int PendingMainThreadActions { get; }
    }
}
