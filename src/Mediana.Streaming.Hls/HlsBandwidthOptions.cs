// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    public record HlsBandwidthOptions
    {
        public HlsPeakBandwidthMode PeakMode { get; init; }
        public Action<string>? WarningHandler { get; init; }

        internal void Validate()
        {
            _ = PeakMode switch
            {
                HlsPeakBandwidthMode.RequireRfcWindow or HlsPeakBandwidthMode.EstimateShortPresentation => true,
                _ => throw new ArgumentOutOfRangeException(nameof(PeakMode))
            };
        }
    }
}
