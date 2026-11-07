// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.Hevc
{
    internal static class HevcInterpolation
    {
        private static readonly int[][] Luma =
        [
            [0, 0, 0, 64, 0, 0, 0, 0],
            [-1, 4, -10, 58, 17, -5, 1, 0],
            [-1, 4, -11, 40, 40, -11, 4, -1],
            [0, 1, -5, 17, 58, -10, 4, -1]
        ];
        private static readonly int[][] Chroma =
        [
            [0, 64, 0, 0], [-2, 58, 10, -2], [-4, 54, 16, -2], [-6, 46, 28, -4],
            [-4, 36, 36, -4], [-4, 28, 46, -6], [-2, 16, 54, -4], [-2, 10, 58, -2]
        ];

        public static int GetWorkingSampleCount(int width, int height, bool chroma)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
            int extra = chroma ? 3 : 7;
            return checked(width * (height + extra) + width + extra);
        }

        /// <summary>Produces unrounded fourteen-bit prediction values; source coordinates are full samples.
        /// Luma fractions are quarters, chroma fractions are eighths. Working and output spans must be disjoint.</summary>
        public static void Predict<TSample, TFormat>(ReadOnlySamplePlaneView<TSample> source, int sourceX, int sourceY,
            int fractionX, int fractionY, bool chroma, int bitDepth, int width, int height,
            Span<int> output, Span<int> working)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            int count = GetWorkingSampleCount(width, height, chroma);
            if (bitDepth is not (8 or 10)) { throw new ArgumentOutOfRangeException(nameof(bitDepth)); }
            int phases = chroma ? 8 : 4;
            if ((uint)fractionX >= phases || (uint)fractionY >= phases) { throw new ArgumentOutOfRangeException(nameof(fractionX)); }
            output = output[..checked(width * height)]; working = working[..count];
            if (output.Overlaps(working)) { throw new ArgumentException("HEVC interpolation working storage overlaps its output.", nameof(working)); }
            if (MemoryMarshal.AsBytes(output).Overlaps(MemoryMarshal.AsBytes(source.Samples)) ||
                MemoryMarshal.AsBytes(working).Overlaps(MemoryMarshal.AsBytes(source.Samples))) { throw new ArgumentException("HEVC interpolation output overlaps its reference samples.", nameof(output)); }
            int headroom = 14 - bitDepth;
            if ((fractionX | fractionY) == 0)
            {
                for (int y = 0; y < height; y++)
                {
                    ReadOnlySpan<TSample> row = source.Row(Clamp((long)sourceY + y, source.Height));
                    for (int x = 0; x < width; x++) { output[y * width + x] = default(TFormat).Read(row[Clamp((long)sourceX + x, source.Width)]) << headroom; }
                }
                return;
            }
            int[][] filters = chroma ? Chroma : Luma;
            ReadOnlySpan<int> horizontal = filters[fractionX], vertical = filters[fractionY];
            int taps = horizontal.Length, margin = taps / 2 - 1;
            int rows = fractionY == 0 ? height : height + taps - 1;
            Span<int> intermediate = working[..checked(rows * width)];
            Span<int> expanded = working.Slice(checked(width * (height + taps - 1)), width + taps - 1);
            int firstY = fractionY == 0 ? 0 : -margin;
            for (int y = 0; y < rows; y++)
            {
                ReadOnlySpan<TSample> row = source.Row(Clamp((long)sourceY + firstY + y, source.Height));
                Span<int> destination = intermediate.Slice(y * width, width);
                if (fractionX == 0)
                {
                    for (int x = 0; x < width; x++) { destination[x] = default(TFormat).Read(row[Clamp((long)sourceX + x, source.Width)]) << headroom; }
                }
                else
                {
                    for (int x = 0; x < expanded.Length; x++) { expanded[x] = default(TFormat).Read(row[Clamp((long)sourceX - margin + x, source.Width)]); }
                    Filter(expanded, 1, horizontal, bitDepth - 8, destination);
                }
            }
            if (fractionY == 0) { intermediate.CopyTo(output); return; }
            for (int y = 0; y < height; y++)
            {
                Filter(intermediate[(y * width)..], width, vertical, 6, output.Slice(y * width, width));
            }
        }

        private static void Filter(ReadOnlySpan<int> source, int tapStride, ReadOnlySpan<int> coefficients, int shift, Span<int> output)
        {
            int x = 0;
            if (Vector.IsHardwareAccelerated)
            {
                for (; x <= output.Length - Vector<int>.Count; x += Vector<int>.Count)
                {
                    Vector<int> sum = Vector<int>.Zero;
                    for (int tap = 0; tap < coefficients.Length; tap++)
                    {
                        sum += new Vector<int>(source.Slice(x + tap * tapStride, Vector<int>.Count)) * new Vector<int>(coefficients[tap]);
                    }
                    Vector.ShiftRightArithmetic(sum, shift).CopyTo(output[x..]);
                }
            }
            for (; x < output.Length; x++)
            {
                int sum = 0;
                for (int tap = 0; tap < coefficients.Length; tap++) { sum += source[x + tap * tapStride] * coefficients[tap]; }
                output[x] = sum >> shift;
            }
        }

        private static int Clamp(long coordinate, int size) => (int)Math.Clamp(coordinate, 0, size - 1);
    }
}
