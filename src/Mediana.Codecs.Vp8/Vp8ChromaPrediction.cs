// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal static class Vp8ChromaPrediction
    {
        internal static void Predict(Vp8ChromaMode mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner,
            bool hasTop, bool hasLeft, Span<byte> output, int size = 8)
        {
            switch (mode)
            {
                case Vp8ChromaMode.Dc:
                    int sum = 0;
                    for (int index = 0; index < size; index++) { if (hasTop) { sum += top[index]; } if (hasLeft) { sum += left[index]; } }
                    int count = (hasTop ? size : 0) + (hasLeft ? size : 0);
                    output.Fill(count == 0 ? (byte)128 : (byte)((sum + count / 2) / count));
                    return;
                case Vp8ChromaMode.Vertical:
                    for (int row = 0; row < size; row++) { top.CopyTo(output.Slice(row * size, size)); }
                    return;
                case Vp8ChromaMode.Horizontal:
                    for (int row = 0; row < size; row++) { output.Slice(row * size, size).Fill(left[row]); }
                    return;
                case Vp8ChromaMode.TrueMotion:
                    for (int row = 0; row < size; row++)
                    {
                        for (int column = 0; column < size; column++) { output[row * size + column] = (byte)Math.Clamp(left[row] + top[column] - corner, 0, 255); }
                    }
                    return;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }
}
