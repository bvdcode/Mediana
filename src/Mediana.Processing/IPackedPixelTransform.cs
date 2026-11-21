// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    internal interface IPackedPixelTransform
    {
        void WritePixel(Span<byte> destination, ReadOnlySpan<byte> source);
    }
}
