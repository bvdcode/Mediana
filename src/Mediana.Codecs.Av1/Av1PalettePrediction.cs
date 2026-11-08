// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly ref struct Av1PalettePrediction<T>(ReadOnlySpan<T> lumaColors, ReadOnlySpan<T> chromaColors,
        ReadOnlySpan<byte> lumaMap, ReadOnlySpan<byte> chromaMap) where T : unmanaged
    {
        private readonly ReadOnlySpan<T> y = lumaColors;
        private readonly ReadOnlySpan<T> uv = chromaColors;
        private readonly ReadOnlySpan<byte> yMap = lumaMap;
        private readonly ReadOnlySpan<byte> uvMap = chromaMap;

        internal ReadOnlySpan<T> Colors(int plane) => plane switch
        {
            0 => y,
            1 => uv[..(uv.Length / 2)],
            2 => uv[(uv.Length / 2)..],
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };

        internal ReadOnlySpan<byte> Map(int plane) => plane switch
        {
            0 => yMap,
            1 or 2 => uvMap,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };
    }
}
