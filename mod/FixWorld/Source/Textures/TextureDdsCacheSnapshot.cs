// SPDX-License-Identifier: MPL-2.0
using System;
using FixWorld.Telemetry;

namespace FixWorld.Textures
{
    internal sealed class TextureDdsCacheSnapshot
    {
        internal TextureDdsCacheSnapshot(
            bool enabled,
            long hits,
            long misses,
            long created,
            long invalidated,
            long excluded,
            long unsupported,
            long budgetSkipped,
            long failed,
            long buildMilliseconds,
            long cacheBytes,
            long maxCacheBytes,
            int workerCount,
            long workerPreparedMods,
            long workerAppliedMods,
            long workerFallbackMods, bool busy = false, int retryMods = 0, string error = null, string root = null,
            long minimumFreeBytes = 0, long availableFreeBytes = -1, long effectiveBudgetBytes = 0,
            string reserveWarning = null, bool maximumOverridden = false, bool minimumFreeOverridden = false,
            bool maintenancePending = false, int plannedMods = 0, int processedMods = 0, string currentMod = null)
        {
            Enabled = enabled;
            Busy = busy;
            RetryMods = retryMods;
            Error = error;
            Root = root;
            MinimumFreeBytes = minimumFreeBytes;
            AvailableFreeBytes = availableFreeBytes;
            EffectiveBudgetBytes = effectiveBudgetBytes;
            ReserveWarning = reserveWarning;
            MaximumOverridden = maximumOverridden;
            MinimumFreeOverridden = minimumFreeOverridden;
            MaintenancePending = maintenancePending;
            PlannedMods = plannedMods;
            ProcessedMods = processedMods;
            CurrentMod = currentMod;
            Hits = hits;
            Misses = misses;
            Created = created;
            Invalidated = invalidated;
            Excluded = excluded;
            Unsupported = unsupported;
            BudgetSkipped = budgetSkipped;
            Failed = failed;
            BuildMilliseconds = buildMilliseconds;
            CacheBytes = cacheBytes;
            MaxCacheBytes = maxCacheBytes;
            WorkerCount = workerCount;
            WorkerPreparedMods = workerPreparedMods;
            WorkerAppliedMods = workerAppliedMods;
            WorkerFallbackMods = workerFallbackMods;
        }

        internal bool Busy { get; }
        internal int RetryMods { get; }
        internal string Error { get; }
        internal string Root { get; }
        internal long MinimumFreeBytes { get; }
        internal long AvailableFreeBytes { get; }
        internal long EffectiveBudgetBytes { get; }
        internal string ReserveWarning { get; }
        internal bool MaximumOverridden { get; }
        internal bool MinimumFreeOverridden { get; }
        internal bool MaintenancePending { get; }
        internal int PlannedMods { get; }
        internal int ProcessedMods { get; }
        internal int RemainingMods => Math.Max(0, PlannedMods - ProcessedMods);
        internal string CurrentMod { get; }
        internal static TelemetryContract<TextureDdsCacheSnapshot> Contract { get; } = new("fixworld.dds", 1, (data, writer) =>
        {
            writer.Value("enabled", data.Enabled);
            writer.Value("worker_running", data.Busy);
            writer.Value("cache_root", data.Root);
            writer.Value("last_error", data.Error);
            writer.Value("failed_mods_retryable", data.RetryMods);
            writer.Counter("cache_hits", data.Hits);
            writer.Counter("cache_misses", data.Misses);
            writer.Counter("created", data.Created);
            writer.Counter("failed", data.Failed);
            writer.Counter("invalidated", data.Invalidated);
            writer.Counter("excluded", data.Excluded);
            writer.Counter("unsupported", data.Unsupported);
            writer.Counter("budget_skipped", data.BudgetSkipped);
            writer.Counter("build_ms", data.BuildMilliseconds);
            writer.Value("cache_bytes", data.CacheBytes);
            writer.Value("max_cache_bytes", data.MaxCacheBytes);
            writer.Value("minimum_free_bytes", data.MinimumFreeBytes);
            writer.Value("available_free_bytes", data.AvailableFreeBytes);
            writer.Value("effective_budget_bytes", data.EffectiveBudgetBytes);
            writer.Value("reserve_warning", data.ReserveWarning);
            writer.Value("maximum_environment_override", data.MaximumOverridden);
            writer.Value("minimum_free_environment_override", data.MinimumFreeOverridden);
            writer.Value("maintenance_pending", data.MaintenancePending);
            writer.Value("batch_planned_mods", data.PlannedMods);
            writer.Value("batch_processed_mods", data.ProcessedMods);
            writer.Value("batch_remaining_mods", data.RemainingMods);
            writer.Value("current_mod", data.CurrentMod);
            writer.Value("workers", data.WorkerCount);
        });

        public bool Enabled { get; private set; }
        public long Hits { get; private set; }
        public long Misses { get; private set; }
        public long Created { get; private set; }
        public long Invalidated { get; private set; }
        public long Excluded { get; private set; }
        public long Unsupported { get; private set; }
        public long BudgetSkipped { get; private set; }
        public long Failed { get; private set; }
        public long BuildMilliseconds { get; private set; }
        public long CacheBytes { get; private set; }
        public long MaxCacheBytes { get; private set; }
        public int WorkerCount { get; private set; }
        public long WorkerPreparedMods { get; private set; }
        public long WorkerAppliedMods { get; private set; }
        public long WorkerFallbackMods { get; private set; }

        internal static TextureDdsCacheSnapshot Disabled(int workerCount)
        {
            return new TextureDdsCacheSnapshot(
                false,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                0L,
                workerCount,
                0L,
                0L,
                0L);
        }
    }
}
