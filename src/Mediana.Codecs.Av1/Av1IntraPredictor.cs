// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1IntraPredictor
    {
        internal static void Predict<T>(Span<T> destination, int stride, int width, int height, int visibleWidth, int visibleHeight,
            ReadOnlySpan<T> above, ReadOnlySpan<T> left, int mode) where T : unmanaged, IBinaryInteger<T>
        {
            switch (mode)
            {
                case 9:
                case 10:
                case 11:
                    Smooth(destination, stride, width, height, visibleWidth, visibleHeight, above, left, mode);
                    break;
                case 12:
                    Paeth(destination, stride, visibleWidth, visibleHeight, above, left);
                    break;
                default:
                    throw new InvalidDataException("The AV1 non-directional prediction mode is invalid.");
            }
        }

        private static void Smooth<T>(Span<T> destination, int stride, int width, int height, int visibleWidth, int visibleHeight,
            ReadOnlySpan<T> above, ReadOnlySpan<T> left, int mode) where T : unmanaged, IBinaryInteger<T>
        {
            ReadOnlySpan<byte> weightsX = Weights.Slice(width - 4, width);
            ReadOnlySpan<byte> weightsY = Weights.Slice(height - 4, height);
            int below = int.CreateTruncating(left[height]);
            int right = int.CreateTruncating(above[width]);
            for (int y = 0; y < visibleHeight; y++)
            {
                Span<T> row = destination.Slice(y * stride, visibleWidth);
                for (int x = 0; x < row.Length; x++)
                {
                    int vertical = weightsY[y] * int.CreateTruncating(above[x + 1]) + (256 - weightsY[y]) * below;
                    int horizontal = weightsX[x] * int.CreateTruncating(left[y + 1]) + (256 - weightsX[x]) * right;
                    row[x] = mode switch
                    {
                        9 => T.CreateTruncating((vertical + horizontal + 256) >> 9),
                        10 => T.CreateTruncating((vertical + 128) >> 8),
                        11 => T.CreateTruncating((horizontal + 128) >> 8),
                        _ => throw new InvalidDataException("The AV1 smooth prediction mode is invalid.")
                    };
                }
            }
        }

        private static void Paeth<T>(Span<T> destination, int stride, int visibleWidth, int visibleHeight,
            ReadOnlySpan<T> above, ReadOnlySpan<T> left) where T : unmanaged, IBinaryInteger<T>
        {
            int corner = int.CreateTruncating(above[0]);
            for (int y = 0; y < visibleHeight; y++)
            {
                Span<T> row = destination.Slice(y * stride, visibleWidth);
                int prior = int.CreateTruncating(left[y + 1]);
                for (int x = 0; x < row.Length; x++)
                {
                    int top = int.CreateTruncating(above[x + 1]);
                    int basis = top + prior - corner;
                    int leftDistance = Math.Abs(basis - prior);
                    int topDistance = Math.Abs(basis - top);
                    int cornerDistance = Math.Abs(basis - corner);
                    int predicted;
                    if (leftDistance <= topDistance && leftDistance <= cornerDistance) { predicted = prior; }
                    else if (topDistance <= cornerDistance) { predicted = top; }
                    else { predicted = corner; }
                    row[x] = T.CreateTruncating(predicted);
                }
            }
        }

        private static ReadOnlySpan<byte> Weights =>
        [
            255, 149, 85, 64, 255, 197, 146, 105, 73, 50, 37, 32, 255, 225, 196, 170,
            145, 123, 102, 84, 68, 54, 43, 33, 26, 20, 17, 16, 255, 240, 225, 210,
            196, 182, 169, 157, 145, 133, 122, 111, 101, 92, 83, 74, 66, 59, 52, 45,
            39, 34, 29, 25, 21, 17, 14, 12, 10, 9, 8, 8, 255, 248, 240, 233,
            225, 218, 210, 203, 196, 189, 182, 176, 169, 163, 156, 150, 144, 138, 133, 127,
            121, 116, 111, 106, 101, 96, 91, 86, 82, 77, 73, 69, 65, 61, 57, 54,
            50, 47, 44, 41, 38, 35, 32, 29, 27, 25, 22, 20, 18, 16, 15, 13,
            12, 10, 9, 8, 7, 6, 6, 5, 5, 4, 4, 4,
        ];
    }
}
