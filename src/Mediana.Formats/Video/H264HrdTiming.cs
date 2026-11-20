// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>Bit lengths declared by HRD for buffering-period and picture-timing messages.</summary>
    public readonly record struct H264HrdTiming(int InitialRemovalDelayBits, int RemovalDelayBits,
        int OutputDelayBits, int TimeOffsetBits);
}
