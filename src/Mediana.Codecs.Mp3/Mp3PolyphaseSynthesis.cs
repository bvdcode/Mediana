// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3PolyphaseSynthesis
    {
        private static readonly float[] Window = CreateWindow();

        public static void Process(ReadOnlySpan<double> subbands, Span<float> history, ref int position,
            Span<float> output, int channels, int channel)
        {
            Span<double> samples = stackalloc double[32];
            Span<float> summed = stackalloc float[32];
            for (int time = 0; time < 18; time++)
            {
                for (int subband = 0; subband < 32; subband++)
                {
                    samples[subband] = subbands[subband * 18 + time];
                }
                position = (position - 64) & 1023;
                Mp3Dct32.Transform(samples);
                for (int index = 0; index < 16; index++)
                {
                    history[position + index] = (float)samples[16 + index];
                    history[position + 48 + index] = (float)-samples[index];
                }
                history[position + 16] = 0;
                for (int index = 0; index < 31; index++)
                {
                    history[position + 17 + index] = (float)-samples[31 - index];
                }
                summed.Clear();
                for (int block = 0; block < 8; block++)
                {
                    ReadOnlySpan<float> first = history.Slice((position + block * 128) & 1023, 32);
                    ReadOnlySpan<float> second = history.Slice((position + block * 128 + 96) & 1023, 32);
                    ReadOnlySpan<float> firstWindow = Window.AsSpan(block * 64, 32);
                    ReadOnlySpan<float> secondWindow = Window.AsSpan(block * 64 + 32, 32);
                    for (int sample = 0; sample < 32; sample += Vector<float>.Count)
                    {
                        Vector<float> value = new Vector<float>(summed[sample..])
                            + new Vector<float>(first[sample..]) * new Vector<float>(firstWindow[sample..])
                            + new Vector<float>(second[sample..]) * new Vector<float>(secondWindow[sample..]);
                        value.CopyTo(summed[sample..]);
                    }
                }
                for (int sample = 0; sample < 32; sample++)
                {
                    float pcm = summed[sample];
                    if (!float.IsFinite(pcm))
                    {
                        throw new InvalidDataException("MP3 synthesis produced a nonfinite PCM sample.");
                    }
                    output[(time * 32 + sample) * channels + channel] = pcm;
                }
            }
        }

        private static float[] CreateWindow()
        {
            ReadOnlySpan<int> coefficients = Mp3SynthesisWindow.Coefficients;
            float[] result = new float[coefficients.Length];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = coefficients[index] / 65536.0f;
            }
            return result;
        }
    }
}
