// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    public record MediaProcessingResult(long Packets, IReadOnlyList<MediaTrackResult> Tracks);
}
