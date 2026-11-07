// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9InterPrediction
    {
        internal static int WorkspaceLength(int width, int height, int stepY) => checked(width * ((((height - 1) * stepY + 15) >> 4) + 8));

        internal static void Predict<T>(ReadOnlySpan<T> reference, int referenceStride, int referenceWidth, int referenceHeight,
            Span<T> destination, int destinationStride, int width, int height, int startX, int startY, int stepX, int stepY,
            int filter, int depth, Span<T> workspace, bool compound) where T : unmanaged, IBinaryInteger<T>
        {
            if (stepX <= 0 || stepX > 32 || stepY <= 0 || stepY > 32 || width <= 0 || width > 64 || height <= 0 || height > 64 || filter < 0 || filter > 3)
            { throw new InvalidDataException("Invalid VP9 fractional prediction geometry."); }
            if (stepX == 16 && stepY == 16 && (startX & 15) == 0 && (startY & 15) == 0)
            { Integer(reference, referenceStride, referenceWidth, referenceHeight, destination, destinationStride, width, height, startX >> 4, startY >> 4, compound); return; }
            int intermediateHeight = (((height - 1) * stepY + 15) >> 4) + 8, max = (1 << depth) - 1;
            workspace = workspace[..checked(width * intermediateHeight)];
            ReadOnlySpan<short> filters = Vp9Tables.Interpolation.Slice(filter * 128, 128);
            for (int row = 0; row < intermediateHeight; row++)
            {
                ReadOnlySpan<T> source = reference.Slice(Math.Clamp((startY >> 4) + row - 3, 0, referenceHeight - 1) * referenceStride, referenceWidth);
                Span<T> horizontal = workspace.Slice(row * width, width);
                int x = startX;
                for (int column = 0; column < width; column++, x += stepX)
                {
                    ReadOnlySpan<short> coefficients = filters.Slice((x & 15) * 8, 8);
                    int center = x >> 4, sum = 0;
                    if ((x & 15) == 0) { horizontal[column] = source[Math.Clamp(center, 0, referenceWidth - 1)]; continue; }
                    if (center >= 3 && center + 4 < referenceWidth)
                    { for (int tap = 0; tap < 8; tap++) { sum += coefficients[tap] * int.CreateTruncating(source[center + tap - 3]); } }
                    else
                    { for (int tap = 0; tap < 8; tap++) { sum += coefficients[tap] * int.CreateTruncating(source[Math.Clamp(center + tap - 3, 0, referenceWidth - 1)]); } }
                    horizontal[column] = T.CreateTruncating(Math.Clamp((sum + 64) >> 7, 0, max));
                }
            }
            for (int row = 0; row < height; row++)
            {
                int y = (startY & 15) + row * stepY, center = y >> 4;
                ReadOnlySpan<short> coefficients = filters.Slice((y & 15) * 8, 8);
                Span<T> output = destination.Slice(row * destinationStride, width);
                for (int column = 0; column < width; column++)
                {
                    int value;
                    if ((y & 15) == 0) { value = int.CreateTruncating(workspace[(center + 3) * width + column]); }
                    else
                    {
                        int sum = 0;
                        for (int tap = 0; tap < 8; tap++) { sum += coefficients[tap] * int.CreateTruncating(workspace[(center + tap) * width + column]); }
                        value = Math.Clamp((sum + 64) >> 7, 0, max);
                    }
                    output[column] = T.CreateTruncating(compound ? (int.CreateTruncating(output[column]) + value + 1) >> 1 : value);
                }
            }
        }
        private static void Integer<T>(ReadOnlySpan<T> source, int stride, int sourceWidth, int sourceHeight,
            Span<T> destination, int destinationStride, int width, int height, int x, int y, bool compound) where T : unmanaged, IBinaryInteger<T>
        {
            for (int row = 0; row < height; row++)
            {
                ReadOnlySpan<T> input = source.Slice(Math.Clamp(y + row, 0, sourceHeight - 1) * stride, sourceWidth);
                Span<T> output = destination.Slice(row * destinationStride, width);
                if (x >= 0 && x <= sourceWidth - width)
                {
                    input = input.Slice(x, width);
                    if (!compound) { input.CopyTo(output); continue; }
                    int column = 0;
                    if (Vector.IsHardwareAccelerated && Vector<T>.IsSupported)
                    {
                        for (; column <= width - Vector<T>.Count; column += Vector<T>.Count)
                        {
                            Vector<T> first = new(input.Slice(column)), second = new(output.Slice(column)), difference = first ^ second;
                            ((first & second) + (difference >> 1) + (difference & Vector<T>.One)).CopyTo(output.Slice(column));
                        }
                    }
                    for (; column < width; column++) { output[column] = T.CreateTruncating((int.CreateTruncating(input[column]) + int.CreateTruncating(output[column]) + 1) >> 1); }
                    continue;
                }
                for (int column = 0; column < width; column++)
                {
                    int value = int.CreateTruncating(input[Math.Clamp(x + column, 0, sourceWidth - 1)]);
                    output[column] = T.CreateTruncating(compound ? (value + int.CreateTruncating(output[column]) + 1) >> 1 : value);
                }
            }
        }
    }
}
