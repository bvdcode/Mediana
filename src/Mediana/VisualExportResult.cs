// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana
{
    public record VisualExportResult(long Frames, long BytesWritten, int Width, int Height,
        MediaTimestamp? FirstTimestamp, MediaDuration? ProcessedDuration);
}
