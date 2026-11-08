// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacPsDecorrelator
    {
        public const int DelaySamples = 91 * 14;
        public const int StateSamples = DelaySamples + 50 * 15;
        private static readonly Complex[] Rotations20 = CreateRotations(20, 30);
        private static readonly Complex[] Rotations34 = CreateRotations(34, 50);

        public static void Process(ReadOnlySpan<Complex> input, Span<Complex> output, Span<Complex> state,
            Span<double> transients, int clock, int stereoBands)
        {
            ReadOnlySpan<sbyte> map = AacPsBandMapping.Parameters(stereoBands);
            ReadOnlySpan<Complex> rotations;
            int allpass;
            int shortDelay;
            int decayCutoff;
            switch (stereoBands)
            {
                case 20:
                    rotations = Rotations20;
                    allpass = 30;
                    shortDelay = 42;
                    decayCutoff = 10;
                    break;
                case 34:
                    rotations = Rotations34;
                    allpass = 50;
                    shortDelay = 62;
                    decayCutoff = 32;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stereoBands));
            }
            Span<double> power = stackalloc double[34];
            power.Clear();
            for (int band = 0; band < map.Length; band++)
            {
                int parameter = ParameterIndex(map[band]);
                Complex sample = input[band];
                power[parameter] += sample.Real * sample.Real + sample.Imaginary * sample.Imaginary;
            }
            for (int parameter = 0; parameter < stereoBands; parameter++)
            {
                double peak = Math.Max(power[parameter], transients[parameter] * 0.76592833836465);
                transients[parameter] = peak;
                double energy = transients[34 + parameter] += 0.25 * (power[parameter] - transients[34 + parameter]);
                double difference = transients[68 + parameter] += 0.25 * (peak - power[parameter] - transients[68 + parameter]);
                power[parameter] = 1.5 * difference > energy ? energy / (1.5 * difference) : 1;
            }
            ReadOnlySpan<int> lengths = [3, 4, 5];
            ReadOnlySpan<double> coefficients = [0.65143905753106, 0.56471812200776, 0.48954165955695];
            for (int band = 0; band < map.Length; band++)
            {
                Complex sample;
                if (band < allpass)
                {
                    int index = band * 14 + clock % 2;
                    sample = state[index] * rotations[band * 4];
                    state[index] = input[band];
                    double decay = Math.Max(0, 1 - 0.05 * Math.Max(0, band - decayCutoff));
                    for (int link = 0; link < 3; link++)
                    {
                        index = DelaySamples + band * 15 + link * 5 + clock % lengths[link];
                        double coefficient = coefficients[link] * decay;
                        Complex next = state[index] * rotations[band * 4 + link + 1] - coefficient * sample;
                        state[index] = sample + coefficient * next;
                        sample = next;
                    }
                }
                else
                {
                    int delay = band < shortDelay ? 14 : 1;
                    int index = band * 14 + clock % delay;
                    sample = state[index];
                    state[index] = input[band];
                }
                output[band] = sample * power[ParameterIndex(map[band])];
            }
        }

        public static void ClearUpperBands(Span<Complex> state, int firstBand, int stereoBands)
        {
            int bands = AacPsBandMapping.Parameters(stereoBands).Length;
            int allpass = stereoBands switch
            {
                20 => 30,
                34 => 50,
                _ => throw new ArgumentOutOfRangeException(nameof(stereoBands))
            };
            for (int band = firstBand; band < bands; band++)
            {
                state.Slice(band * 14, 14).Clear();
                if (band < allpass)
                {
                    state.Slice(DelaySamples + band * 15, 15).Clear();
                }
            }
        }

        public static int ParameterIndex(int value) => value < 0 ? -value - 1 : value;

        private static Complex[] CreateRotations(int stereoBands, int allpassBands)
        {
            Complex[] result = new Complex[allpassBands * 4];
            ReadOnlySpan<double> delays = [0.39, 0.43, 0.75, 0.347];
            for (int band = 0; band < allpassBands; band++)
            {
                double frequency = AacPsBandMapping.CenterFrequency(band, stereoBands);
                for (int delay = 0; delay < delays.Length; delay++)
                {
                    (double sine, double cosine) = Math.SinCos(-Math.PI * delays[delay] * frequency);
                    result[band * 4 + delay] = new Complex(cosine, sine);
                }
            }
            return result;
        }
    }
}
