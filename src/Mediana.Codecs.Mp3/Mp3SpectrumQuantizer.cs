// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3SpectrumQuantizer
    {
        public const int Region0 = 7;
        public const int Region1 = 5;

        public static Mp3QuantizedGranule Quantize(ReadOnlySpan<double> spectrum, int sampleRate, int budget, Span<int> quantized)
        {
            Span<double> powers = stackalloc double[576];
            for (int index = 0; index < 576; index++)
            {
                powers[index] = Math.Pow(Math.Abs(spectrum[index]), 0.75);
            }
            int lower = 0;
            int upper = 255;
            while (lower < upper)
            {
                int gain = (lower + upper) / 2;
                Mp3QuantizedGranule candidate = Apply(spectrum, powers, gain, sampleRate, quantized);
                if (candidate.Bits > budget) { lower = gain + 1; }
                else { upper = gain; }
            }
            Mp3QuantizedGranule result = Apply(spectrum, powers, lower, sampleRate, quantized);
            if (result.Bits > budget)
            {
                throw new NotSupportedException("The MP3 spectrum cannot fit the configured bit budget and gain range.");
            }
            return result;
        }

        public static void Write(ref Mp3BitWriter writer, Mp3QuantizedGranule info, ReadOnlySpan<int> values, int sampleRate)
        {
            ReadOnlySpan<int> bands = Mp3ScaleFactorBands.Long(sampleRate);
            int first = Math.Min(info.BigValues * 2, bands[Region0 + 1]);
            int second = Math.Min(info.BigValues * 2, bands[Region0 + Region1 + 2]);
            Mp3HuffmanEncoder.Write(ref writer, values[..first], info.Table0);
            Mp3HuffmanEncoder.Write(ref writer, values[first..second], info.Table1);
            Mp3HuffmanEncoder.Write(ref writer, values[second..(info.BigValues * 2)], info.Table2);
        }

        private static Mp3QuantizedGranule Apply(ReadOnlySpan<double> spectrum, ReadOnlySpan<double> powers,
            int gain, int sampleRate, Span<int> quantized)
        {
            double factor = Math.Pow(2, (210 - gain) * 0.1875);
            int end = 0;
            for (int index = 0; index < 576; index++)
            {
                double value = powers[index] * factor + 0.4054;
                if (value >= 8207) { return new(int.MaxValue, 0, gain, 0, 0, 0); }
                int magnitude = (int)value;
                quantized[index] = spectrum[index] < 0 ? -magnitude : magnitude;
                if (magnitude != 0) { end = (index + 2) & ~1; }
            }
            ReadOnlySpan<int> bands = Mp3ScaleFactorBands.Long(sampleRate);
            int first = Math.Min(end, bands[Region0 + 1]);
            int second = Math.Min(end, bands[Region0 + Region1 + 2]);
            int a = Mp3HuffmanEncoder.Select(quantized[..first], out int table0);
            int b = Mp3HuffmanEncoder.Select(quantized[first..second], out int table1);
            int c = Mp3HuffmanEncoder.Select(quantized[second..end], out int table2);
            if (a == int.MaxValue || b == int.MaxValue || c == int.MaxValue) { return new(int.MaxValue, 0, gain, 0, 0, 0); }
            return new(a + b + c, end / 2, gain, table0, table1, table2);
        }
    }
}
