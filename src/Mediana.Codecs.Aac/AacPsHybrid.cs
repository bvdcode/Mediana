// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacPsHybrid
    {
        private static readonly Complex[] SplitTwo = CreateKernel(2, [0, 0.01899487526049, 0, -0.07293139167538,
            0, 0.30596630545168, 0.5, 0.30596630545168, 0, -0.07293139167538, 0, 0.01899487526049, 0], false);
        private static readonly Complex[] SplitEight = CreateKernel(8, [0.00746082949812, 0.02270420949825,
            0.04546865930473, 0.07266113929591, 0.09885108575264, 0.11793710567217, 0.125,
            0.11793710567217, 0.09885108575264, 0.07266113929591, 0.04546865930473, 0.02270420949825, 0.00746082949812], true);
        private static readonly Complex[] SplitTwelve = CreateKernel(12, [0.04081179924692, 0.03812810994926,
            0.05144908135699, 0.06399831151592, 0.07428313801106, 0.08100347892914, 1.0 / 12,
            0.08100347892914, 0.07428313801106, 0.06399831151592, 0.05144908135699, 0.03812810994926, 0.04081179924692], true);
        private static readonly Complex[] SplitEightFine = CreateKernel(8, [0.01565675600122, 0.03752716391991,
            0.05417891378782, 0.08417044116767, 0.10307344158036, 0.12222452249753, 0.125,
            0.12222452249753, 0.10307344158036, 0.08417044116767, 0.05417891378782, 0.03752716391991, 0.01565675600122], true);
        private static readonly Complex[] SplitFour = CreateKernel(4, [-0.05908211155639, -0.04871498374946,
            0, 0.07778723915851, 0.16486303567403, 0.23279856662996, 0.25,
            0.23279856662996, 0.16486303567403, 0.07778723915851, 0, -0.04871498374946, -0.05908211155639], true);

        public static void Analyze(ReadOnlySpan<Complex> input, ReadOnlySpan<Complex> history,
            int slot, int stereoBands, Span<Complex> output)
        {
            if ((uint)slot >= 32)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
            Span<Complex> split = stackalloc Complex[12];
            switch (stereoBands)
            {
                case 20:
                    Filter(input, history, slot, 0, SplitEight, split[..8]);
                    output[0] = split[6];
                    output[1] = split[7];
                    output[2] = split[0];
                    output[3] = split[1];
                    output[4] = split[2] + split[5];
                    output[5] = split[3] + split[4];
                    Filter(input, history, slot, 1, SplitTwo, split[..2]);
                    output[6] = split[1];
                    output[7] = split[0];
                    Filter(input, history, slot, 2, SplitTwo, output.Slice(8, 2));
                    input.Slice(slot * 64 + 3, 61).CopyTo(output[10..]);
                    break;
                case 34:
                    Filter(input, history, slot, 0, SplitTwelve, output[..12]);
                    Filter(input, history, slot, 1, SplitEightFine, output.Slice(12, 8));
                    for (int band = 2; band < 5; band++)
                    {
                        Filter(input, history, slot, band, SplitFour, output.Slice(20 + (band - 2) * 4, 4));
                    }
                    input.Slice(slot * 64 + 5, 59).CopyTo(output[32..]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stereoBands));
            }
        }

        public static void Synthesize(ReadOnlySpan<Complex> input, int stereoBands, Span<Complex> output)
        {
            output.Clear();
            int offset;
            int lowBands;
            switch (stereoBands)
            {
                case 20:
                    output[0] = Sum(input[..6]);
                    output[1] = input[6] + input[7];
                    output[2] = input[8] + input[9];
                    offset = 10;
                    lowBands = 3;
                    break;
                case 34:
                    output[0] = Sum(input[..12]);
                    output[1] = Sum(input.Slice(12, 8));
                    for (int band = 2; band < 5; band++)
                    {
                        output[band] = Sum(input.Slice(20 + (band - 2) * 4, 4));
                    }
                    offset = 32;
                    lowBands = 5;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stereoBands));
            }
            input.Slice(offset, 64 - lowBands).CopyTo(output[lowBands..]);
        }

        private static void Filter(ReadOnlySpan<Complex> input, ReadOnlySpan<Complex> history,
            int slot, int band, ReadOnlySpan<Complex> kernel, Span<Complex> output)
        {
            for (int split = 0; split < output.Length; split++)
            {
                Complex sum = Complex.Zero;
                for (int tap = 0; tap < 13; tap++)
                {
                    int time = slot + tap - 6;
                    Complex sample = time < 0 ? history[(time + 6) * 5 + band] : input[time * 64 + band];
                    sum += sample * kernel[split * 13 + tap];
                }
                output[split] = sum;
            }
        }

        private static Complex Sum(ReadOnlySpan<Complex> values)
        {
            Complex sum = Complex.Zero;
            foreach (Complex value in values)
            {
                sum += value;
            }
            return sum;
        }

        private static Complex[] CreateKernel(int bands, ReadOnlySpan<double> prototype, bool complex)
        {
            Complex[] result = new Complex[bands * 13];
            for (int band = 0; band < bands; band++)
            {
                for (int tap = 0; tap < 13; tap++)
                {
                    double angle = -2 * Math.PI / bands * (band + (complex ? 0.5 : 0)) * (tap - 6);
                    (double sine, double cosine) = Math.SinCos(angle);
                    result[band * 13 + tap] = prototype[tap] * new Complex(cosine, complex ? sine : 0);
                }
            }
            return result;
        }
    }
}
