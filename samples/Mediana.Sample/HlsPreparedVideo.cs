// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Streaming.Hls;

namespace Mediana.Sample
{
    internal record HlsPreparedVideo(EncodedStreamInfo Configuration, HlsVariant Variant,
        IReadOnlyList<HlsMediaSegment> Segments, IReadOnlyList<MpegTsSegmentState?> States);
}
