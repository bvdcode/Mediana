// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacChannelEncoder
    {
        public static int Quantize(ReadOnlySpan<double> spectrum, Span<double> powered, Span<int> quantized,
            Span<byte> books, Span<byte> scales, int frequencyIndex, int budget, CancellationToken token)
        {
            double maximum = 0;
            for (int bin = 0; bin < spectrum.Length; bin++)
            {
                powered[bin] = Math.Pow(Math.Abs(spectrum[bin]), 0.75);
                maximum = Math.Max(maximum, powered[bin]);
            }
            double required = maximum == 0 ? 0 : Math.Ceiling(100 + 16.0 / 3 * Math.Log2(maximum / AacQuantizer.MaximumMagnitude));
            if (!double.IsFinite(required) || required > AacQuantizer.MaximumScale)
            {
                throw new NotSupportedException("The PCM magnitude exceeds the AAC quantizer range.");
            }
            ReadOnlySpan<int> boundaries = AacBandTables.Get(frequencyIndex, false);
            int count = boundaries.Length - 1;
            Span<AacBandQuantization> bands = stackalloc AacBandQuantization[count];
            Span<AacBandQuantization> candidates = stackalloc AacBandQuantization[count * 2];
            AacBandAllocator allocation = new(spectrum, powered, boundaries, quantized, bands, candidates);
            allocation.Allocate(Math.Max(0, (int)required), budget, token);
            int gain = 0;
            bool first = true;
            for (int band = 0; band < count; band++)
            {
                token.ThrowIfCancellationRequested();
                AacBandQuantization selected = bands[band];
                int start = boundaries[band];
                int length = boundaries[band + 1] - start;
                _ = AacQuantizer.Quantize(spectrum.Slice(start, length), powered.Slice(start, length), quantized.Slice(start, length), selected.Scale);
                books[band] = selected.Book;
                scales[band] = (byte)selected.Scale;
                if (first && selected.Book != 0)
                {
                    gain = selected.Scale;
                    first = false;
                }
            }
            return gain;
        }

        public static int Count(ReadOnlySpan<AacBandQuantization> bands)
        {
            int total = 11;
            int previous = -1;
            for (int band = 0; band < bands.Length; band++)
            {
                AacBandQuantization current = bands[band];
                if (!double.IsFinite(current.Error))
                {
                    return int.MaxValue;
                }
                if (current.Book == 0)
                {
                    continue;
                }
                int difference = previous < 0 ? 0 : current.Scale - previous;
                if (difference is < -60 or > 60)
                {
                    return int.MaxValue;
                }
                total += current.Bits + AacHuffmanEncoder.CountSymbol(0, difference + 60);
                previous = current.Scale;
            }
            int start = 0;
            while (start < bands.Length)
            {
                int end = start + 1;
                while (end < bands.Length && bands[end].Book == bands[start].Book)
                {
                    end++;
                }
                total += 4 + 5 * ((end - start) / 31 + 1);
                start = end;
            }
            return total;
        }

        public static void WriteWindow(ref AacBitWriter bits, int frequencyIndex)
        {
            bits.Write(0, 4);
            bits.Write((uint)(AacBandTables.Get(frequencyIndex, false).Length - 1), 6);
            bits.Write(0, 1);
        }

        public static void Write(ref AacBitWriter bits, int gain, ReadOnlySpan<int> quantized,
            ReadOnlySpan<byte> books, ReadOnlySpan<byte> scales, int frequencyIndex, bool commonWindow)
        {
            bits.Write((uint)gain, 8);
            if (!commonWindow)
            {
                WriteWindow(ref bits, frequencyIndex);
            }
            ReadOnlySpan<int> bands = AacBandTables.Get(frequencyIndex, false);
            int start = 0;
            while (start < bands.Length - 1)
            {
                int end = start + 1;
                while (end < bands.Length - 1 && books[end] == books[start])
                {
                    end++;
                }
                bits.Write(books[start], 4);
                int remaining = end - start;
                while (remaining >= 31)
                {
                    bits.Write(31, 5);
                    remaining -= 31;
                }
                bits.Write((uint)remaining, 5);
                start = end;
            }
            int previous = gain;
            for (int band = 0; band < bands.Length - 1; band++)
            {
                if (books[band] != 0)
                {
                    AacHuffmanEncoder.WriteSymbol(ref bits, 0, scales[band] - previous + 60);
                    previous = scales[band];
                }
            }
            bits.Write(0, 3);
            for (int band = 0; band < bands.Length - 1; band++)
            {
                if (books[band] != 0)
                {
                    AacHuffmanEncoder.Write(ref bits, books[band], quantized[bands[band]..bands[band + 1]]);
                }
            }
        }
    }
}
