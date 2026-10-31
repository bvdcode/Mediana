// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    internal static class AacAnalysisTestInput
    {
        public static void VerifyTransform(int sequence, int shape, int previousShape)
        {
            double[] input = Enumerable.Range(0, 2048).Select(index => Math.Sin(index * 0.019) +
                0.07 * Math.Cos(index * 0.701) + (index % 127 == 0 ? 0.5 : 0)).ToArray();
            foreach (int index in new[] { 447, 448, 575, 576, 1023, 1024, 1471, 1472, 1599, 1600, 2047 })
            {
                input[index] = 1.25;
            }
            double[] original = input.ToArray();
            double[] spectrum = new double[1025];
            double[] windowed = new double[2049];
            double[] folded = new double[1025];
            Complex[] work = new Complex[513];
            spectrum.AsSpan().Fill(double.NaN);
            windowed.AsSpan().Fill(double.NaN);
            folded.AsSpan().Fill(double.NaN);
            work.AsSpan().Fill(new(double.NaN, double.NaN));
            spectrum[^1] = windowed[^1] = folded[^1] = 123;
            work[^1] = new(123, 456);
            AacAnalysis.Transform(input, sequence, shape, previousShape, spectrum.AsSpan(0, 1024),
                windowed.AsSpan(0, 2048), folded.AsSpan(0, 1024), work.AsSpan(0, 512), default);
            int windows = sequence == 2 ? 8 : 1;
            int size = 1024 / windows;
            for (int window = 0; window < windows; window++)
            {
                double[] weighted = new double[size * 2];
                for (int sample = 0; sample < weighted.Length; sample++)
                {
                    int start = sequence == 2 ? 448 + window * 128 : 0;
                    double weight = sequence == 2
                        ? ShortWeight(sample, window == 0 ? previousShape : shape, shape)
                        : LongWeight(sample, sequence, previousShape, shape);
                    weighted[sample] = input[start + sample] * weight;
                }
                for (int bin = 0; bin < size; bin++)
                {
                    double expected = 0;
                    for (int sample = 0; sample < weighted.Length; sample++)
                    {
                        expected += 2 * weighted[sample] * Math.Cos(Math.PI / size * (sample + 0.5 + size / 2) * (bin + 0.5));
                    }
                    RequireClose(expected, spectrum[window * size + bin], 5e-8, "AAC analysis coefficient");
                }
            }
            if (!original.AsSpan().SequenceEqual(input) || spectrum[^1] != 123 || windowed[^1] != 123 ||
                folded[^1] != 123 || work[^1] != new Complex(123, 456))
            {
                throw new InvalidDataException("AAC analysis changed source samples or exceeded a workspace boundary.");
            }
        }

        public static void VerifyTransitions()
        {
            int[] sequences = [0, 1, 2, 2, 3, 0, 1, 2, 3, 1, 2, 2, 3, 0, 0];
            double[] input = new double[2048];
            double[] spectrum = new double[1024];
            double[] windowed = new double[2048];
            double[] folded = new double[1024];
            double[] time = new double[2048];
            double[] overlap = new double[1024];
            double[] output = new double[1024];
            Complex[] work = new Complex[512];
            int previousShape = 0;
            for (int frame = 0; frame < sequences.Length; frame++)
            {
                for (int sample = 0; sample < input.Length; sample++)
                {
                    int position = (frame - 1) * 1024 + sample;
                    input[sample] = position >= 0 && position < (sequences.Length - 1) * 1024
                        ? Signal(position) * 32768 : 0;
                }
                int shape = frame % 2;
                AacAnalysis.Transform(input, sequences[frame], shape, previousShape, spectrum, windowed, folded, work, default);
                AacSynthesis.Process(spectrum, sequences[frame], shape, previousShape, overlap, output, time, work, default);
                for (int sample = 0; sample < output.Length; sample++)
                {
                    double expected = frame == 0 ? 0 : Signal((frame - 1) * 1024 + sample);
                    RequireClose(expected, output[sample], 2e-12, "AAC switched-window sample");
                }
                previousShape = shape;
            }
            foreach (double value in overlap)
            {
                RequireClose(0, value, 1e-7, "AAC final overlap");
            }
        }

        private static double Signal(int sample) => 0.3 * Math.Sin(sample * 0.07) + 0.13 * Math.Cos(sample * 0.0017)
            + (sample % 1024 is 0 or 447 or 448 or 575 or 576 or 1023 ? 0.2 : 0);

        private static double ShortWeight(int sample, int previousShape, int shape) => sample < 128
            ? Half(sample, 128, previousShape) : Half(255 - sample, 128, shape);

        private static double LongWeight(int sample, int sequence, int previousShape, int shape)
        {
            if (sample < 1024)
            {
                if (sequence == 3)
                {
                    return sample switch
                    {
                        < 448 => 0,
                        < 576 => Half(sample - 448, 128, previousShape),
                        _ => 1
                    };
                }
                return Half(sample, 1024, previousShape);
            }
            if (sequence == 1)
            {
                return sample switch
                {
                    < 1472 => 1,
                    < 1600 => Half(1599 - sample, 128, shape),
                    _ => 0
                };
            }
            return Half(2047 - sample, 1024, shape);
        }

        private static double Half(int index, int size, int shape) => shape switch
        {
            0 => Math.Sin(Math.PI / (2 * size) * (index + 0.5)),
            1 => AacWindows.Get(size, shape)[index],
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        private static void RequireClose(double expected, double actual, double tolerance, string description)
        {
            if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidDataException($"{description} differs: expected {expected:G17}, actual {actual:G17}.");
            }
        }
    }
}
