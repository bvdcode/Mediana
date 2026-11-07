// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltSpreadingRotation
    {
        public static int GetFactor(CeltSpreading spreading) => spreading switch
        {
            CeltSpreading.None => 0,
            CeltSpreading.Light => 15,
            CeltSpreading.Normal => 10,
            CeltSpreading.Aggressive => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(spreading))
        };

        public static void ApplyInverse(Span<float> vector, int pulses, int blocks, CeltSpreading spreading)
        {
            int factor = GetFactor(spreading);
            if (factor == 0 || (long)pulses * 2 >= vector.Length) { return; }
            float gain = vector.Length / (float)(vector.Length + factor * pulses);
            float angle = gain * gain * .5f;
            float cosine = (float)Math.Cos(Math.PI * .5 * angle);
            float sine = (float)Math.Cos(Math.PI * .5 * (1f - angle));
            int width = vector.Length / blocks;
            int stride = 0;
            if (width >= 8) { stride = (int)Math.Round(Math.Sqrt(width)); }
            for (int block = 0; block < blocks; block++)
            {
                Span<float> samples = vector.Slice(block * width, width);
                if (stride > 0) { Rotate(samples, stride, sine, cosine); }
                Rotate(samples, 1, cosine, sine);
            }
        }

        private static void Rotate(Span<float> samples, int stride, float cosine, float sine)
        {
            for (int index = 0; index < samples.Length - stride; index++)
            {
                float left = samples[index], right = samples[index + stride];
                samples[index] = cosine * left - sine * right;
                samples[index + stride] = cosine * right + sine * left;
            }
            for (int index = samples.Length - 2 * stride - 1; index >= 0; index--)
            {
                float left = samples[index], right = samples[index + stride];
                samples[index] = cosine * left - sine * right;
                samples[index + stride] = cosine * right + sine * left;
            }
        }
    }
}
