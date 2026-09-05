// SPDX-License-Identifier: MPL-2.0
using System;

namespace FixWorld.Profiling
{
    public enum ProfileAggregationMode
    {
        Inline,
        Buffered
    }

    public sealed class ProfilerOptions
    {
        private static readonly TimeSpan DefaultPublishInterval =
            TimeSpan.FromMilliseconds(500);

        public static ProfilerOptions Inline { get; } =
            new(ProfileAggregationMode.Inline);

        public static ProfilerOptions Buffered { get; } =
            new(ProfileAggregationMode.Buffered);

        public ProfilerOptions(
            ProfileAggregationMode aggregationMode,
            bool enabled = true,
            TimeSpan? publishInterval = null)
        {
            TimeSpan interval = publishInterval ?? DefaultPublishInterval;
            if (aggregationMode is not ProfileAggregationMode.Inline and
                not ProfileAggregationMode.Buffered)
            {
                throw new ArgumentOutOfRangeException(nameof(aggregationMode));
            }

            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(publishInterval));
            }

            AggregationMode = aggregationMode;
            Enabled = enabled;
            PublishInterval = interval;
        }

        public ProfileAggregationMode AggregationMode { get; }

        public bool Enabled { get; }

        public TimeSpan PublishInterval { get; }
    }
}
