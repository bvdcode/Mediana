// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.MpegTs
{
    /// <summary>Limits and common timeline shift for a single-program transport stream.</summary>
    public class MpegTsWriteOptions
    {
        public int MaximumPacketBytes { get; init; } = 16 * 1024 * 1024;
        public int MaximumConfigurationBytes { get; init; } = 1024 * 1024;
        /// <summary>Added to all PTS/DTS. The default one second accommodates ordinary decoder priming.</summary>
        public MediaDuration TimestampOffset { get; init; } = new(1, new TimeBase(1, 1));
        public MediaDuration MaximumTimestampGap { get; init; } = new(60, new TimeBase(1, 1));

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPacketBytes);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPacketBytes, int.MaxValue - 19);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumConfigurationBytes);
            TimestampOffset.TimeBase.Validate();
            MaximumTimestampGap.TimeBase.Validate();
            if (MaximumTimestampGap.Value == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumTimestampGap));
            }
        }
    }
}
