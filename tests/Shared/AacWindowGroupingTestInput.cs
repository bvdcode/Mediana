// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    internal static class AacWindowGroupingTestInput
    {
        public static void VerifyFrequency(int frequency)
        {
            for (int grouping = 0; grouping < 5; grouping++)
            {
                for (int shape = 0; shape < 2; shape++)
                {
                    VerifySyntax(frequency, grouping, shape, true);
                    VerifySyntax(frequency, grouping, shape, false);
                }
            }
        }

        public static void VerifyAllocation(int frequency, int grouping, int channels, int budget)
        {
            AacEncodingWindow window = new(frequency, 2, 0, Groups(grouping), new int[AacSpectrumQuantizer.ChannelBands + 1]);
            double[] spectrum = new double[channels * 1024];
            double[] ungrouped = new double[1024];
            for (int channel = 0; channel < channels; channel++)
            {
                for (int bin = 0; bin < 1024; bin++)
                {
                    ungrouped[bin] = Math.Sin((bin + channel * 29) * 0.941) * (bin / 128 == 4 ? 1000000 : 1000);
                }
                window.Group<double>(ungrouped, spectrum.AsSpan(channel * 1024, 1024));
            }
            int[] quantized = new int[spectrum.Length];
            byte[] books = new byte[channels * AacSpectrumQuantizer.ChannelBands];
            byte[] scales = new byte[books.Length];
            int[] gains = new int[channels];
            AacSpectrumQuantizer.Quantize(spectrum, new double[spectrum.Length], quantized, books, scales, gains,
                window, budget, default);
            int total = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                total += AacBandAllocationTestInput.VerifySyntax(frequency, window, gains[channel],
                    quantized.AsSpan(channel * 1024, 1024).ToArray(),
                    books.AsSpan(channel * AacSpectrumQuantizer.ChannelBands, AacSpectrumQuantizer.ChannelBands).ToArray(),
                    scales.AsSpan(channel * AacSpectrumQuantizer.ChannelBands, AacSpectrumQuantizer.ChannelBands).ToArray(), budget, true);
            }
            if (total > budget)
            {
                throw new InvalidDataException("AAC short-window allocation exceeds the complete element budget.");
            }
        }

        private static void VerifySyntax(int frequency, int grouping, int shape, bool commonWindow)
        {
            int[] groups = Groups(grouping);
            AacEncodingWindow window = new(frequency, 2, shape, groups, new int[AacSpectrumQuantizer.ChannelBands + 1]);
            int[] source = Enumerable.Range(0, 1024).ToArray();
            int[] packed = new int[1025];
            int[] restored = new int[1025];
            packed[^1] = restored[^1] = 12345;
            window.Group<int>(source, packed.AsSpan(0, 1024));
            window.Ungroup<int>(packed.AsSpan(0, 1024), restored.AsSpan(0, 1024));
            if (!source.AsSpan().SequenceEqual(restored.AsSpan(0, 1024)) || packed[^1] != 12345 || restored[^1] != 12345)
            {
                throw new InvalidDataException("AAC short-window grouping changed a coefficient or exceeded its storage.");
            }
            ReadOnlySpan<int> table = AacBandTables.Get(frequency, true);
            int firstWindow = 0;
            int position = 0;
            foreach (int length in groups)
            {
                for (int band = 0; band < table.Length - 1; band++)
                {
                    for (int item = 0; item < length; item++)
                    {
                        for (int bin = table[band]; bin < table[band + 1]; bin++)
                        {
                            if (packed[position++] != source[(firstWindow + item) * 128 + bin])
                            {
                                throw new InvalidDataException("AAC grouped spectral order differs from group/band/window/coefficient order.");
                            }
                        }
                    }
                }
                firstWindow += length;
            }
            if (position != 1024 || window.Boundaries[^1] != 1024)
            {
                throw new InvalidDataException("AAC grouping did not retain a complete short-window spectrum.");
            }
            byte[] books = new byte[AacSpectrumQuantizer.ChannelBands];
            byte[] scales = new byte[books.Length];
            for (int band = 0; band < window.BandCount; band++)
            {
                books[band] = 11;
                scales[band] = (byte)(100 + band * 17 % 11);
            }
            for (int bin = 0; bin < 1024; bin++)
            {
                packed[bin] = (int)(((uint)(bin + 1) * 747796405u + 2891336453u) & 63) - 32;
            }
            AacBandAllocationTestInput.VerifySyntax(frequency, window, scales[0], packed.AsSpan(0, 1024).ToArray(), books,
                scales, 16384, commonWindow);
            books.AsSpan().Clear();
            scales.AsSpan().Clear();
            int count = AacBandAllocationTestInput.VerifySyntax(frequency, window, 0, new int[1024], books, scales, 512, commonWindow);
            int expected = 11 + groups.Length * (4 + 3 * ((table.Length - 1) / 7 + 1));
            if (!commonWindow)
            {
                expected += 15;
            }
            if (count != expected)
            {
                throw new InvalidDataException("AAC short-window sections crossed a group or omitted an escape terminator.");
            }
        }

        private static int[] Groups(int grouping) => grouping switch
        {
            0 => [8],
            1 => [1, 1, 1, 1, 1, 1, 1, 1],
            2 => [1, 3, 2, 2],
            3 => [3, 3, 2],
            4 => [4, 4],
            _ => throw new ArgumentOutOfRangeException(nameof(grouping))
        };
    }
}
