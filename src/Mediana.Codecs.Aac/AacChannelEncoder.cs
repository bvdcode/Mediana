// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacChannelEncoder
    {
        public static int Count(ReadOnlySpan<AacBandQuantization> bands, int bandsPerGroup, int sectionBits)
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
            int escape = (1 << sectionBits) - 1;
            for (int firstBand = 0; firstBand < bands.Length; firstBand += bandsPerGroup)
            {
                int groupEnd = Math.Min(bands.Length, firstBand + bandsPerGroup);
                int start = firstBand;
                while (start < groupEnd)
                {
                    int end = start + 1;
                    while (end < groupEnd && bands[end].Book == bands[start].Book)
                    {
                        end++;
                    }
                    total += 4 + sectionBits;
                    for (int remaining = end - start; remaining >= escape; remaining -= escape)
                    {
                        total += sectionBits;
                    }
                    start = end;
                }
            }
            return total;
        }

        public static void WriteWindow(ref AacBitWriter bits, scoped AacEncodingWindow window)
        {
            bits.Write((uint)(window.Sequence << 1 | window.Shape), 4);
            if (window.ShortWindow)
            {
                bits.Write((uint)window.BandsPerGroup, 4);
                bits.Write((uint)window.Grouping, 7);
            }
            else
            {
                bits.Write((uint)window.BandsPerGroup, 6);
                bits.Write(0, 1);
            }
        }

        public static void Write(ref AacBitWriter bits, int gain, ReadOnlySpan<int> quantized,
            ReadOnlySpan<byte> books, ReadOnlySpan<byte> scales, scoped AacEncodingWindow window, bool commonWindow)
        {
            bits.Write((uint)gain, 8);
            if (!commonWindow)
            {
                WriteWindow(ref bits, window);
            }
            ReadOnlySpan<int> bands = window.Boundaries;
            int escape = (1 << window.SectionLengthBits) - 1;
            for (int firstBand = 0; firstBand < window.BandCount; firstBand += window.BandsPerGroup)
            {
                int groupEnd = firstBand + window.BandsPerGroup;
                int start = firstBand;
                while (start < groupEnd)
                {
                    int end = start + 1;
                    while (end < groupEnd && books[end] == books[start])
                    {
                        end++;
                    }
                    bits.Write(books[start], 4);
                    int remaining = end - start;
                    while (remaining >= escape)
                    {
                        bits.Write((uint)escape, window.SectionLengthBits);
                        remaining -= escape;
                    }
                    bits.Write((uint)remaining, window.SectionLengthBits);
                    start = end;
                }
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
