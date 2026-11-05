// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.Webp
{
    internal static class Vp8IntraPrediction
    {
        private static ReadOnlySpan<byte> Codes => [0, 2, 6, 28, 30, 58, 59, 62, 126, 127];
        private static ReadOnlySpan<byte> Lengths => [1, 2, 3, 5, 5, 6, 6, 6, 7, 7];
        private static ReadOnlySpan<sbyte> Tree => [0, 2, -1, 4, -2, 6, 8, 12, -3, 10, -5, -6, -4, 14, -7, 16, -8, -9];

        internal static Vp8IntraMode Select(ReadOnlySpan<byte> source, int offset, int stride,
            ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner, Span<byte> prediction)
        {
            Span<byte> input = stackalloc byte[16];
            Span<byte> candidate = stackalloc byte[16];
            for (int row = 0; row < 4; row++) { source.Slice(offset + row * stride, 4).CopyTo(input[(row * 4)..]); }
            int best = int.MaxValue;
            Vp8IntraMode selected = Vp8IntraMode.Dc;
            for (int mode = 0; mode < Codes.Length; mode++)
            {
                Predict((Vp8IntraMode)mode, top, left, corner, candidate);
                int score = SquaredError(input, candidate);
                if (score < best)
                {
                    best = score;
                    selected = (Vp8IntraMode)mode;
                    candidate.CopyTo(prediction);
                }
            }
            return selected;
        }

        internal static void Write(Vp8BooleanWriter writer, Vp8IntraMode mode, int above, int left)
        {
            int index = (int)mode;
            int node = 0;
            int start = (above * 10 + left) * 9;
            for (int bit = Lengths[index] - 1; bit >= 0; bit--)
            {
                bool value = ((Codes[index] >> bit) & 1) != 0;
                writer.Write(value, Vp8ProbabilityTables.BlockModes[start + node / 2]);
                node = Tree[node + (value ? 1 : 0)];
            }
        }

        internal static int SquaredError(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction)
        {
            if (Vector128.IsHardwareAccelerated)
            {
                Vector128<byte> a = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source));
                Vector128<byte> b = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(prediction));
                Vector128<short> low = (Vector128.WidenLower(a) - Vector128.WidenLower(b)).AsInt16();
                Vector128<short> high = (Vector128.WidenUpper(a) - Vector128.WidenUpper(b)).AsInt16();
                Vector128<int> d0 = Vector128.WidenLower(low);
                Vector128<int> d1 = Vector128.WidenUpper(low);
                Vector128<int> d2 = Vector128.WidenLower(high);
                Vector128<int> d3 = Vector128.WidenUpper(high);
                return Vector128.Sum(d0 * d0 + d1 * d1 + d2 * d2 + d3 * d3);
            }
            int error = 0;
            for (int index = 0; index < 16; index++)
            {
                int difference = source[index] - prediction[index];
                error += difference * difference;
            }
            return error;
        }

        internal static void Predict(Vp8IntraMode mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner, Span<byte> output)
        {
            switch (mode)
            {
                case Vp8IntraMode.Dc:
                    int sum = 4;
                    for (int index = 0; index < 4; index++) { sum += top[index] + left[index]; }
                    output.Fill((byte)(sum >> 3));
                    return;
                case Vp8IntraMode.TrueMotion:
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++) { output[y * 4 + x] = (byte)Math.Clamp(left[y] + top[x] - corner, 0, 255); }
                    }
                    return;
                case Vp8IntraMode.Vertical:
                    for (int x = 0; x < 4; x++)
                    {
                        byte value = Average3(x == 0 ? corner : top[x - 1], top[x], top[x + 1]);
                        for (int y = 0; y < 4; y++) { output[y * 4 + x] = value; }
                    }
                    return;
                case Vp8IntraMode.Horizontal:
                    for (int y = 0; y < 4; y++) { output.Slice(y * 4, 4).Fill(Average3(y == 0 ? corner : left[y - 1], left[y], left[Math.Min(y + 1, 3)])); }
                    return;
                case Vp8IntraMode.DownLeft:
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++) { output[y * 4 + x] = Average3(top[x + y], top[x + y + 1], top[Math.Min(x + y + 2, 7)]); }
                    }
                    return;
                case Vp8IntraMode.DownRight:
                    Span<byte> diagonal = stackalloc byte[9];
                    for (int index = 0; index < 4; index++) { diagonal[index] = left[3 - index]; diagonal[index + 5] = top[index]; }
                    diagonal[4] = corner;
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++) { int center = 4 + x - y; output[y * 4 + x] = Average3(diagonal[center - 1], diagonal[center], diagonal[center + 1]); }
                    }
                    return;
                case Vp8IntraMode.VerticalRight: VerticalRight(top, left, corner, output); return;
                case Vp8IntraMode.HorizontalDown:
                    Span<byte> transposed = stackalloc byte[16];
                    VerticalRight(left, top, corner, transposed);
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++) { output[y * 4 + x] = transposed[x * 4 + y]; }
                    }
                    return;
                case Vp8IntraMode.VerticalLeft:
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            int start = x + (y >> 1);
                            output[y * 4 + x] = (y & 1) == 0 ? Average2(top[start], top[start + 1]) : Average3(top[start], top[start + 1], top[start + 2]);
                        }
                    }
                    output[11] = Average3(top[4], top[5], top[6]);
                    output[15] = Average3(top[5], top[6], top[7]);
                    return;
                case Vp8IntraMode.HorizontalUp:
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            int start = y + (x >> 1);
                            output[y * 4 + x] = start >= 3 ? left[3] : (x & 1) == 0 ? Average2(left[start], left[start + 1]) :
                                Average3(left[start], left[start + 1], left[Math.Min(start + 2, 3)]);
                        }
                    }
                    return;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private static void VerticalRight(ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner, Span<byte> output)
        {
            for (int x = 0; x < 4; x++)
            {
                output[x] = Average2(x == 0 ? corner : top[x - 1], top[x]);
                output[x + 4] = Average3(x switch { 0 => left[0], 1 => corner, _ => top[x - 2] }, x == 0 ? corner : top[x - 1], top[x]);
            }
            output[8] = Average3(left[1], left[0], corner);
            output[12] = Average3(left[2], left[1], left[0]);
            output[..3].CopyTo(output[9..]);
            output.Slice(4, 3).CopyTo(output[13..]);
        }

        private static byte Average2(int a, int b) => (byte)((a + b + 1) >> 1);
        private static byte Average3(int a, int b, int c) => (byte)((a + 2 * b + c + 2) >> 2);
    }
}
