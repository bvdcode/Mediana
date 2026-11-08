// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1LoopFilterKernel
    {
        internal static void Apply(Span<byte> pixels, int origin, int step, int size, bool chroma, int limit, int boundaryLimit, int threshold)
        {
            int radius = size == 4 ? 2 : chroma ? 3 : size == 8 ? 4 : 7;
            Span<int> samples = stackalloc int[14];
            for (int index = -radius; index < radius; index++) { samples[index + 7] = pixels[origin + index * step]; }
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
                flat &= Math.Abs(samples[6 - index] - p0) <= 1 && Math.Abs(samples[7 + index] - q0) <= 1;
            }
            if (!flat)
            {
                Narrow(pixels, origin, step, highVariance, p1, p0, q0, q1);
                return;
            }
            bool wide = size == 16;
            for (int index = 4; index < 7 && wide; index++)
            {
                wide &= Math.Abs(samples[6 - index] - p0) <= 1 && Math.Abs(samples[7 + index] - q0) <= 1;
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
            for (int index = -taps; index < taps; index++) { pixels[origin + index * step] = (byte)filtered[index + taps]; }
        }

        private static void Narrow(Span<byte> pixels, int origin, int step, bool highVariance, int p1, int p0, int q0, int q1)
        {
            int filter = highVariance ? Clamp(p1 - q1) : 0;
            filter = Clamp(filter + 3 * (q0 - p0));
            int right = Clamp(filter + 4) >> 3;
            int left = Clamp(filter + 3) >> 3;
            pixels[origin] = (byte)(Clamp(q0 - 128 - right) + 128);
            pixels[origin - step] = (byte)(Clamp(p0 - 128 + left) + 128);
            if (!highVariance)
            {
                int outside = (right + 1) >> 1;
                pixels[origin + step] = (byte)(Clamp(q1 - 128 - outside) + 128);
                pixels[origin - step * 2] = (byte)(Clamp(p1 - 128 + outside) + 128);
            }
        }

        private static int Clamp(int value) => Math.Clamp(value, -128, 127);
    }
}
