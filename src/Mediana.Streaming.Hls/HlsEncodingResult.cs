// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    public record HlsEncodingResult(long InputFrames, int VideoRenditions, int SegmentsPerRendition, long AudioPackets, long BytesWritten);
}
