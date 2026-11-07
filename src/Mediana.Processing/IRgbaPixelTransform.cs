// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    internal interface IRgbaPixelTransform
    {
        void WritePixel(Span<byte> destination, ReadOnlySpan<byte> source);
    }
}
