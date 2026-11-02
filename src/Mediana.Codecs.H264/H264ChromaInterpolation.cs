// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264ChromaInterpolation
    {
        private const int BlockSize = 8;

        public static void Apply(ReadOnlySpan<byte> samples, int stride, int rows, long x, long y,
            int dx, int dy, int width, int height, Span<byte> output)
        {
            Span<byte> patch = stackalloc byte[(BlockSize + 1) * (BlockSize + 1)];
            int a = (8 - dx) * (8 - dy);
            int b = dx * (8 - dy);
            int c = (8 - dx) * dy;
            int d = dx * dy;
            for (int top = 0; top < height;)
            {
                int tileHeight = Math.Min(BlockSize, height - top);
                for (int left = 0; left < width;)
                {
                    int tileWidth = Math.Min(BlockSize, width - left);
                    int patchStride = tileWidth + 1;
                    H264BorderExtension.Copy(samples, stride, rows, x + left, y + top,
                        patchStride, tileHeight + 1, patch);
                    Span<byte> destination = output[(top * width + left)..];
                    if (tileWidth == Vector64<byte>.Count)
                    {
                        Predict8(patch, patchStride, tileHeight, dx, dy, width, destination);
                    }
                    else
                    {
                        PredictTile(patch, patchStride, tileWidth, tileHeight, a, b, c, d, width, destination);
                    }
                    left += tileWidth;
                }
                top += tileHeight;
            }
        }

        private static void PredictTile(ReadOnlySpan<byte> patch, int stride, int width, int height,
            int a, int b, int c, int d, int outputStride, Span<byte> output)
        {
            for (int row = 0; row < height; row++)
            {
                int position = row * stride;
                int destination = row * outputStride;
                for (int column = 0; column < width; column++)
                {
                    int index = position + column;
                    output[destination + column] = (byte)((a * patch[index] + b * patch[index + 1]
                        + c * patch[index + stride] + d * patch[index + stride + 1] + 32) >> 6);
                }
            }
        }

        private static void Predict8(ReadOnlySpan<byte> patch, int stride, int height, int dx, int dy,
            int outputStride, Span<byte> output)
        {
            Vector128<short> horizontalWeight = Vector128.Create((short)dx);
            Vector128<short> verticalWeight = Vector128.Create((short)dy);
            Vector128<short> top = Horizontal8(patch, 0, horizontalWeight);
            for (int row = 0; row < height; row++)
            {
                Vector128<short> bottom = Horizontal8(patch, (row + 1) * stride, horizontalWeight);
                Vector128<short> value = Vector128.ShiftLeft(top, 3) + (bottom - top) * verticalWeight + Vector128.Create((short)32);
                Vector128<ushort> rounded = Vector128.ShiftRightLogical(value.AsUInt16(), 6);
                Vector128.Narrow(rounded, Vector128<ushort>.Zero).GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)(row * outputStride));
                top = bottom;
            }
        }

        private static Vector128<short> Horizontal8(ReadOnlySpan<byte> patch, int position, Vector128<short> weight)
        {
            Vector128<short> left = Read8(patch, position);
            Vector128<short> right = Read8(patch, position + 1);
            return Vector128.ShiftLeft(left, 3) + (right - left) * weight;
        }

        private static Vector128<short> Read8(ReadOnlySpan<byte> samples, int position) =>
            Vector128.WidenLower(Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(samples), (nuint)position).ToVector128Unsafe()).AsInt16();
    }
}
