// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Vp8;

namespace Mediana.Codecs.Webp
{
    internal static class Vp8IntraModeEncoder
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
                Vp8IntraPrediction.Predict((Vp8IntraMode)mode, top, left, corner, candidate);
                int score = Vp8IntraPrediction.SquaredError(input, candidate);
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

    }
}
