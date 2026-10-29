// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Containers.Mp4
{
    internal record Mp4Movie(TimeBase TimeBase, MediaDuration? Duration, VideoDisplayMatrix DisplayMatrix,
        Mp4TrackInfo[] Tracks, Mp4SampleTable[] Tables, Mp4MetadataValue[] Metadata);
}
