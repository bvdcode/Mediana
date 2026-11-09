// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1TransformSize(int Width, int Height)
    {
        internal int Width4 => Width >> 2;
        internal int Height4 => Height >> 2;
        internal int CodedWidth => Math.Min(32, Width);
        internal int CodedHeight => Math.Min(32, Height);
        internal int Minimum => BitOperations.Log2((uint)Math.Min(Width, Height)) - 2;
        internal int Maximum => BitOperations.Log2((uint)Math.Max(Width, Height)) - 2;
        internal int Context => (Minimum + Maximum + 1) >> 1;
        internal int DequantizationShift => (Width * Height) switch { >= 2048 => 2, >= 512 => 1, _ => 0 };
        internal int Index => (Width, Height) switch
        {
            (4, 4) => 0,
            (8, 8) => 1,
            (16, 16) => 2,
            (32, 32) => 3,
            (64, 64) => 4,
            (4, 8) => 5,
            (8, 4) => 6,
            (8, 16) => 7,
            (16, 8) => 8,
            (16, 32) => 9,
            (32, 16) => 10,
            (32, 64) => 11,
            (64, 32) => 12,
            (4, 16) => 13,
            (16, 4) => 14,
            (8, 32) => 15,
            (32, 8) => 16,
            (16, 64) => 17,
            (64, 16) => 18,
            _ => throw new InvalidDataException("The AV1 transform geometry is invalid.")
        };
        internal static Av1TransformSize FromIndex(int index)
        {
            ReadOnlySpan<byte> widths = [4, 8, 16, 32, 64, 4, 8, 8, 16, 16, 32, 32, 64, 4, 16, 8, 32, 16, 64];
            ReadOnlySpan<byte> heights = [4, 8, 16, 32, 64, 8, 4, 16, 8, 32, 16, 64, 32, 16, 4, 32, 8, 64, 16];
            if ((uint)index >= widths.Length) { throw new InvalidDataException("The AV1 transform size index is invalid."); }
            return new(widths[index], heights[index]);
        }

        internal Av1TransformSize Split => Width == Height ? new(Width >> 1, Height >> 1) :
            Width > Height ? new(Width >> 1, Height) : new(Width, Height >> 1);
    }
}
