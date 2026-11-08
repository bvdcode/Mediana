// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal readonly ref struct Av1PredictionDestination<T>(Span<T> storage, int stride, int originX = 0, int originY = 0) where T : unmanaged, IBinaryInteger<T>
    {
        private readonly Span<T> pixels = storage;
        internal Span<T> Row(int x, int y, int width) => pixels.Slice((y - originY) * stride + x - originX, width);
    }
}
