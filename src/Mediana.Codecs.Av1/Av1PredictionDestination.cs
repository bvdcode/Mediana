// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly ref struct Av1PredictionDestination(Span<byte> storage, int stride, int originX = 0, int originY = 0)
    {
        private readonly Span<byte> pixels = storage;
        internal Span<byte> Row(int x, int y, int width) => pixels.Slice((y - originY) * stride + x - originX, width);
    }
}
