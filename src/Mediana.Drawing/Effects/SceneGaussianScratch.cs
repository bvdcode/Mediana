// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Numerics;
using System.Threading;

namespace Mediana.Drawing
{
    internal class SceneGaussianScratch(PixelBuffer input, int left, int width, int padding, int outputWidth, bool linear, string edge, CancellationToken token)
    {
        private Vector4[] _upper = new Vector4[checked(width + 2 * padding)];
        private Vector4[] _lower = new Vector4[checked(width + 2 * padding)];
        private int _upperY = int.MinValue;
        private int _lowerY = int.MinValue;
        internal Vector4[] Sums { get; } = new Vector4[outputWidth];

        internal void Rows(int y, out ReadOnlySpan<Vector4> upper, out ReadOnlySpan<Vector4> lower)
        {
            int top = Coordinate(y, input.Height), bottom = Coordinate(y + 1, input.Height);
            if (top == _lowerY || (bottom == _upperY && top != _upperY))
            {
                (_upper, _lower) = (_lower, _upper);
                (_upperY, _lowerY) = (_lowerY, _upperY);
            }
            if (top != _upperY)
            {
                ReadRow(top, _upper);
                _upperY = top;
            }
            upper = _upper;
            if (bottom == top)
            {
                lower = _upper;
                return;
            }
            if (bottom != _lowerY)
            {
                ReadRow(bottom, _lower);
                _lowerY = bottom;
            }
            lower = _lower;
        }

        private void ReadRow(int y, Span<Vector4> row)
        {
            token.ThrowIfCancellationRequested();
            if (y < 0)
            {
                row.Clear();
                return;
            }
            ReadOnlySpan<byte> pixels = input.Pixels.Span.Slice(y * input.Width * 4, input.Width * 4);
            for (int i = 0; i < row.Length; i++)
            {
                if ((i & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                int x = left + i - padding;
                if ((uint)x >= input.Width) { x = Coordinate(x, input.Width); }
                row[i] = x < 0 ? Vector4.Zero : ScenePixels.Read(pixels, x * 4, linear);
            }
        }

        private int Coordinate(int value, int length)
        {
            if ((uint)value < length) { return value; }
            return edge switch
            {
                "none" => -1,
                "duplicate" => Math.Clamp(value, 0, length - 1),
                "wrap" => ((value % length) + length) % length,
                _ => throw new ArgumentException("Unknown Gaussian edge mode.", nameof(edge))
            };
        }
    }
}
