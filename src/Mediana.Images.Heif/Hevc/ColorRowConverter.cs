// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Heif.Hevc
{
    internal delegate void ColorRowConverter<T>(ReadOnlySpan<T> luma, ReadOnlySpan<T> cb, ReadOnlySpan<T> cr,
        ColorPlaneLayout layout, Span<byte> rgba) where T : unmanaged;
}
