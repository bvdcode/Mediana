// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Dts
{
    internal static class DtsSubbandDecoder
    {
        private static ReadOnlySpan<int> Levels => [0, 3, 5, 7, 9, 13, 17, 25];
        private static ReadOnlySpan<int> BlockWidths => [0, 7, 10, 12, 13, 15, 17, 19];

        public static void Read(ref DtsBitReader bits, DtsFrameHeader header, DtsChannelCoding channel,
            int group, Span<double> samples, Span<double> history)
        {
            samples.Clear();
            Span<int> quantized = stackalloc int[8];
            ReadOnlySpan<int> steps = header.BitRateCode == 31 ? DtsQuantizationTables.LosslessSteps : DtsQuantizationTables.LossySteps;
            for (int band = 0; band < channel.VectorStart; band++)
            {
                int allocation = channel.Allocations[band];
                ReadSamples(ref bits, allocation, channel.Selectors[allocation], quantized);
                int transient = channel.Transients[band];
                double scale = steps[allocation] / 4194304.0 * channel.Adjustments[allocation] *
                    (transient == 0 || group < transient ? channel.BeforeScales[band] : channel.AfterScales[band]);
                Span<double> previous = history.Slice(band * 4, 4);
                int predictor = channel.Predictors[band];
                for (int i = 0; i < 8; i++)
                {
                    double value = quantized[i] * scale;
                    if (predictor >= 0)
                    {
                        ReadOnlySpan<short> coefficients = DtsPredictionTable.Get(predictor);
                        for (int p = 0; p < 4; p++) { value += coefficients[p] / 8192.0 * previous[p]; }
                    }
                    if (!double.IsFinite(value) || Math.Abs(value) > int.MaxValue)
                    { throw new InvalidDataException("DTS prediction exceeds the sample numeric range."); }
                    previous[..3].CopyTo(previous[1..]);
                    previous[0] = value;
                    samples[i * 32 + band] = value;
                }
            }
            for (int band = channel.VectorStart; band < channel.ActiveBands; band++)
            {
                ReadOnlySpan<sbyte> vector = DtsVectorTable.Get(channel.VectorIndexes[band]).Slice(group * 8, 8);
                double scale = channel.BeforeScales[band] / 16;
                for (int i = 0; i < 8; i++) { samples[i * 32 + band] = vector[i] * scale; }
            }
        }

        private static void ReadSamples(ref DtsBitReader bits, int allocation, int selector, scoped Span<int> output)
        {
            if (allocation == 0) { output.Clear(); return; }
            if (allocation <= 10 && selector < DtsCodebooks.Samples[allocation].Length)
            {
                DtsHuffmanCodebook book = DtsCodebooks.Samples[allocation][selector];
                for (int i = 0; i < 8; i++) { output[i] = book.Read(ref bits); }
            }
            else if (allocation <= 7)
            {
                int radix = Levels[allocation];
                for (int block = 0; block < 2; block++)
                {
                    int value = bits.Read(BlockWidths[allocation]);
                    for (int i = 0; i < 4; i++)
                    {
                        output[block * 4 + i] = value % radix - radix / 2;
                        value /= radix;
                    }
                    if (value != 0) { throw new InvalidDataException("The DTS block code exceeds its quantizer range."); }
                }
            }
            else
            {
                for (int i = 0; i < 8; i++) { output[i] = bits.Signed(allocation - 3); }
            }
        }
    }
}
