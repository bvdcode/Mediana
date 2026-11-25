// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264CoarseMotionSearch(ReadOnlySpan<byte> plane, int stride, int height, Vector128<byte> source,
        (int X, int Y) origin, (int X, int Y) predictor)
    {
        private readonly ReadOnlySpan<byte> reference = plane;
        private const int MotionUnitsPerSample = 4 * H264QuarterLuma.Scale;

        public (int X, int Y) Find(int range)
        {
            int radius = range / H264QuarterLuma.Scale;
            (int X, int Y) best = default;
            int cost = Cost(best);
            (int X, int Y) seed = (RoundPredictor(predictor.X, radius), RoundPredictor(predictor.Y, radius));
            if (seed != best)
            {
                Consider(seed, ref best, ref cost);
            }
            for (int step = Math.Max(1, radius / 2); step > 0; step /= 2)
            {
                (int x, int y) = best;
                for (int dy = -step; dy <= step; dy += step)
                {
                    for (int dx = -step; dx <= step; dx += step)
                    {
                        if ((dx != 0 || dy != 0) && Math.Abs(x + dx) <= radius && Math.Abs(y + dy) <= radius)
                        {
                            Consider((x + dx, y + dy), ref best, ref cost);
                        }
                    }
                }
            }
            return (best.X * MotionUnitsPerSample, best.Y * MotionUnitsPerSample);
        }

        private static int RoundPredictor(int value, int radius) =>
            Math.Clamp((value + Math.Sign(value) * (MotionUnitsPerSample / 2)) / MotionUnitsPerSample, -radius, radius);

        private void Consider((int X, int Y) candidate, ref (int X, int Y) best, ref int cost)
        {
            int next = Cost(candidate);
            if (next < cost)
            {
                best = candidate;
                cost = next;
            }
        }

        private int Cost((int X, int Y) candidate)
        {
            int x = origin.X + candidate.X;
            int y = origin.Y + candidate.Y;
            Vector128<byte> prediction;
            if (x >= 0 && x <= stride - 4 && y >= 0 && y <= height - 4)
            {
                int offset = y * stride + x;
                prediction = Vector128.Create(ReadRow(offset), ReadRow(offset + stride),
                    ReadRow(offset + 2 * stride), ReadRow(offset + 3 * stride)).AsByte();
            }
            else
            {
                prediction = ReadBorder(x, y);
            }
            int bits = H264MotionSearch.VectorBits(candidate.X * MotionUnitsPerSample - predictor.X,
                candidate.Y * MotionUnitsPerSample - predictor.Y);
            return H264SampleDifference.Sum16(source, prediction) + (bits + 1) / 2;
        }

        private Vector128<byte> ReadBorder(int x, int y)
        {
            Span<byte> block = stackalloc byte[16];
            for (int row = 0; row < 4; row++)
            {
                int offset = Math.Clamp(y + row, 0, height - 1) * stride;
                for (int column = 0; column < 4; column++)
                {
                    block[row * 4 + column] = reference[offset + Math.Clamp(x + column, 0, stride - 1)];
                }
            }
            return Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(block));
        }

        private uint ReadRow(int offset) => Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetReference(reference), offset));
    }
}
