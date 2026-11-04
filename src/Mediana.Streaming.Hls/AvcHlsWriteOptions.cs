// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    /// <summary>Starts a new segment at the first IDR at or after TargetDuration from its start.</summary>
    public record AvcHlsWriteOptions : HlsWriteOptions
    {
    }
}
