// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacChannelEncoder
    {
        public static int Quantize(Span<double> spectrum, Span<int> quantized, Span<byte> books,
            int frequencyIndex, int budget, CancellationToken token)
        {
            double maximum = 0;
            for (int bin = 0; bin < spectrum.Length; bin++)
            {
                double value = spectrum[bin];
                double magnitude = Math.Pow(Math.Abs(value), 0.75);
                spectrum[bin] = Math.CopySign(magnitude, value);
                maximum = Math.Max(maximum, magnitude);
            }
            double required = maximum == 0 ? 0 : Math.Ceiling(100 + 16.0 / 3 * Math.Log2(maximum / 8191));
            if (!double.IsFinite(required) || required > 255)
            {
                throw new NotSupportedException("The PCM magnitude exceeds the AAC quantizer range.");
            }
            ReadOnlySpan<int> bands = AacBandTables.Get(frequencyIndex, false);
            for (int gain = Math.Max(0, (int)required); gain <= 255; gain++)
            {
                token.ThrowIfCancellationRequested();
                double factor = Math.Pow(2, -3.0 * (gain - 100) / 16);
                bool representable = true;
                for (int bin = 0; bin < spectrum.Length; bin++)
                {
                    double magnitude = Math.Floor(Math.Abs(spectrum[bin]) * factor + 0.5);
                    if (magnitude > 8191)
                    {
                        representable = false;
                        break;
                    }
                    quantized[bin] = spectrum[bin] < 0 ? -(int)magnitude : (int)magnitude;
                }
                if (representable && SelectBooks(quantized, books, bands) <= budget)
                {
                    return gain;
                }
            }
            throw new NotSupportedException("The PCM frame cannot fit the configured AAC bit budget.");
        }

        private static int SelectBooks(ReadOnlySpan<int> quantized, Span<byte> books, ReadOnlySpan<int> bands)
        {
            int total = 11;
            for (int band = 0; band < bands.Length - 1; band++)
            {
                ReadOnlySpan<int> values = quantized[bands[band]..bands[band + 1]];
                int maximum = 0;
                foreach (int value in values)
                {
                    maximum = Math.Max(maximum, Math.Abs(value));
                }
                if (maximum == 0)
                {
                    books[band] = 0;
                    continue;
                }
                int best = int.MaxValue;
                for (int codebook = 1; codebook <= 11; codebook++)
                {
                    int count = AacHuffmanEncoder.Count(codebook, values);
                    if (count < best)
                    {
                        best = count;
                        books[band] = (byte)codebook;
                    }
                }
                total += best + 1;
            }
            int start = 0;
            while (start < bands.Length - 1)
            {
                int end = start + 1;
                while (end < bands.Length - 1 && books[end] == books[start])
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
            ReadOnlySpan<byte> books, int frequencyIndex, bool commonWindow)
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
            for (int band = 0; band < bands.Length - 1; band++)
            {
                if (books[band] != 0)
                {
                    AacHuffmanEncoder.WriteSymbol(ref bits, 0, 60);
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
