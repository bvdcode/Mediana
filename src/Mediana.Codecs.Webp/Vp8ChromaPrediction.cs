// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal static class Vp8ChromaPrediction
    {
        internal static Vp8ChromaMode Select(ReadOnlySpan<byte> source, Vp8Reconstruction reconstruction, int x,
            bool firstRow, Span<byte> prediction)
        {
            Span<byte> top = stackalloc byte[16];
            Span<byte> left = stackalloc byte[16];
            Span<byte> corners = stackalloc byte[2];
            Span<byte> candidate = stackalloc byte[128];
            for (int plane = 0; plane < 2; plane++)
            {
                corners[plane] = reconstruction.ChromaNeighbors(plane + 1, x, firstRow, top.Slice(plane * 8, 8), left.Slice(plane * 8, 8));
            }
            int best = int.MaxValue;
            Vp8ChromaMode selected = Vp8ChromaMode.Dc;
            for (int mode = 0; mode < 4; mode++)
            {
                for (int plane = 0; plane < 2; plane++)
                {
                    Predict((Vp8ChromaMode)mode, top.Slice(plane * 8, 8), left.Slice(plane * 8, 8), corners[plane],
                        !firstRow, x != 0, candidate.Slice(plane * 64, 64));
                }
                int score = 0;
                for (int offset = 0; offset < 128; offset += 16) { score += Vp8IntraPrediction.SquaredError(source[offset..], candidate[offset..]); }
                if (score < best) { best = score; selected = (Vp8ChromaMode)mode; candidate.CopyTo(prediction); }
            }
            return selected;
        }

        internal static void Write(Vp8BooleanWriter writer, Vp8ChromaMode mode)
        {
            switch (mode)
            {
                case Vp8ChromaMode.Dc: writer.Write(false, 142); break;
                case Vp8ChromaMode.Vertical: writer.Write(true, 142); writer.Write(false, 114); break;
                case Vp8ChromaMode.Horizontal: writer.Write(true, 142); writer.Write(true, 114); writer.Write(false, 183); break;
                case Vp8ChromaMode.TrueMotion: writer.Write(true, 142); writer.Write(true, 114); writer.Write(true, 183); break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        internal static void Predict(Vp8ChromaMode mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner,
            bool hasTop, bool hasLeft, Span<byte> output)
        {
            switch (mode)
            {
                case Vp8ChromaMode.Dc:
                    int sum = 0;
                    for (int index = 0; index < 8; index++) { if (hasTop) { sum += top[index]; } if (hasLeft) { sum += left[index]; } }
                    int count = (hasTop ? 8 : 0) + (hasLeft ? 8 : 0);
                    output.Fill(count == 0 ? (byte)128 : (byte)((sum + count / 2) / count));
                    return;
                case Vp8ChromaMode.Vertical:
                    for (int row = 0; row < 8; row++) { top.CopyTo(output.Slice(row * 8, 8)); }
                    return;
                case Vp8ChromaMode.Horizontal:
                    for (int row = 0; row < 8; row++) { output.Slice(row * 8, 8).Fill(left[row]); }
                    return;
                case Vp8ChromaMode.TrueMotion:
                    for (int row = 0; row < 8; row++)
                    {
                        for (int column = 0; column < 8; column++) { output[row * 8 + column] = (byte)Math.Clamp(left[row] + top[column] - corner, 0, 255); }
                    }
                    return;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }
}
