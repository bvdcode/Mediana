// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;

namespace Mediana.Streaming.Hls
{
    internal readonly record struct Mp4HlsEntry(Mp4HlsSegment Segment, MpegTsSegmentState? State);
}
