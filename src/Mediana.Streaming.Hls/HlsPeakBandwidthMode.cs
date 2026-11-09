// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    public enum HlsPeakBandwidthMode
    {
        RequireRfcWindow,
        /// <summary>Uses the largest complete-segment bitrate when a presentation is too short for the RFC 8216 window.</summary>
        EstimateShortPresentation
    }
}
