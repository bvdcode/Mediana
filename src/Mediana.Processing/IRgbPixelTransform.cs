// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    internal interface IRgbPixelTransform : IPackedPixelTransform
    {
        void Write(Span<byte> destination, double red, double green, double blue, byte alpha);
    }
}
