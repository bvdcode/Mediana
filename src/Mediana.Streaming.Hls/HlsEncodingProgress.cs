// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    public record HlsEncodingProgress(long InputFrames, MediaDuration? ProcessedDuration, long BytesWritten, bool Completed);
}
