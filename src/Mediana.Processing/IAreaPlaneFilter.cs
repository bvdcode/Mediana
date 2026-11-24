// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal interface IAreaPlaneFilter
    {
        void Render(VideoFrame input, int plane, Span<byte> output, int stride,
            ReadOnlySpan<AreaSampleRange> horizontal, ReadOnlySpan<AreaSampleRange> vertical,
            Span<byte> workspace, CancellationToken token);
    }
}
