// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Av1
{
    internal static class Av1LoopFilterKernel
    {
        internal static void Apply<T>(Span<T> pixels, int origin, int step, int size, bool chroma, int limit, int boundaryLimit, int threshold, int bitDepth = 8) where T : unmanaged, IBinaryInteger<T>
        {
            int coefficientShift;
            if (typeof(T) == typeof(byte))
            {
                if (bitDepth != 8) { throw new ArgumentOutOfRangeException(nameof(bitDepth)); }
                coefficientShift = 0;
            }
            else
            {
                coefficientShift = bitDepth switch
                {
                    8 => 0,
                    10 => 2,
                    12 => 4,
                    _ => throw new ArgumentOutOfRangeException(nameof(bitDepth))
                };
            }
            int flatThreshold = 1 << coefficientShift;
            int center = 128 << coefficientShift;
            limit <<= coefficientShift;
            boundaryLimit <<= coefficientShift;
            threshold <<= coefficientShift;
            int radius = size == 4 ? 2 : chroma ? 3 : size == 8 ? 4 : 7;
            Span<int> samples = stackalloc int[14];
            for (int index = -radius; index < radius; index++) { samples[index + 7] = int.CreateTruncating(pixels[origin + index * step]); }
            int p0 = samples[6];
            int q0 = samples[7];
            int p1 = samples[5];
            int q1 = samples[8];
            if (Math.Abs(p0 - q0) * 2 + Math.Abs(p1 - q1) / 2 > boundaryLimit) { return; }
            int adjacent = Math.Min(radius, 4);
            for (int index = 1; index < adjacent; index++)
            {
                if (Math.Abs(samples[6 - index] - samples[7 - index]) > limit ||
                    Math.Abs(samples[7 + index] - samples[6 + index]) > limit) { return; }
            }
            bool highVariance = Math.Abs(p1 - p0) > threshold || Math.Abs(q1 - q0) > threshold;
            bool flat = size >= 8;
            for (int index = 1; index < adjacent && flat; index++)
            {
                flat &= Math.Abs(samples[6 - index] - p0) <= flatThreshold && Math.Abs(samples[7 + index] - q0) <= flatThreshold;
            }
            if (!flat)
            {
                Narrow(pixels, origin, step, highVariance, p1, p0, q0, q1, center);
                return;
            }
            bool wide = size == 16;
            for (int index = 4; index < 7 && wide; index++)
            {
                wide &= Math.Abs(samples[6 - index] - p0) <= flatThreshold && Math.Abs(samples[7 + index] - q0) <= flatThreshold;
            }
            int taps = wide ? 6 : chroma ? 2 : 3;
            int doubled = wide || chroma ? 1 : 0;
            int shift = wide ? 4 : 3;
            Span<int> filtered = stackalloc int[12];
            for (int index = -taps; index < taps; index++)
            {
                int sum = 0;
                for (int delta = -taps; delta <= taps; delta++)
                {
                    int position = Math.Clamp(index + delta, -taps - 1, taps);
                    sum += samples[position + 7] * (Math.Abs(delta) <= doubled ? 2 : 1);
                }
                filtered[index + taps] = (sum + (1 << (shift - 1))) >> shift;
            }
            for (int index = -taps; index < taps; index++) { pixels[origin + index * step] = T.CreateTruncating(filtered[index + taps]); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Narrow<T>(Span<T> pixels, int origin, int step, bool highVariance, int p1, int p0, int q0, int q1, int center) where T : unmanaged, IBinaryInteger<T>
        {
            int filter = highVariance ? Clamp(p1 - q1, center) : 0;
            filter = Clamp(filter + 3 * (q0 - p0), center);
            int right = Clamp(filter + 4, center) >> 3;
            int left = Clamp(filter + 3, center) >> 3;
            pixels[origin] = T.CreateTruncating(Clamp(q0 - center - right, center) + center);
            pixels[origin - step] = T.CreateTruncating(Clamp(p0 - center + left, center) + center);
            if (!highVariance)
            {
                int outside = (right + 1) >> 1;
                pixels[origin + step] = T.CreateTruncating(Clamp(q1 - center - outside, center) + center);
                pixels[origin - step * 2] = T.CreateTruncating(Clamp(p1 - center + outside, center) + center);
            }
        }

        private static int Clamp(int value, int center) => Math.Clamp(value, -center, center - 1);
    }
}
