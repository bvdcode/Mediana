// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Sample
{
    internal record PreparedHlsSampleOptions(long EndMilliseconds, long SegmentMilliseconds, int MaximumWidth)
    {
        public long BufferBudgetBytes { get; init; } = 128 * 1024 * 1024;
        public int MaximumSegments { get; init; } = 10000;
        public int AudioBitRate { get; init; } = 128000;
        public MediaTimestamp Start => new(0, new(1, 1000));
        public MediaTimestamp End => new(EndMilliseconds, new(1, 1000));
        public MediaDuration TargetDuration => new(SegmentMilliseconds, new(1, 1000));
        public long TargetSeconds => checked((SegmentMilliseconds + 999) / 1000);
        public TimeSpan KeyFrameInterval => TimeSpan.FromTicks(checked(SegmentMilliseconds * TimeSpan.TicksPerMillisecond));

        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(EndMilliseconds);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SegmentMilliseconds);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaximumWidth, 2);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BufferBudgetBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSegments);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(AudioBitRate);
            _ = KeyFrameInterval;
            _ = TargetSeconds;
        }
    }
}
