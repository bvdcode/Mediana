// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacChannelEncoder
    {
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
