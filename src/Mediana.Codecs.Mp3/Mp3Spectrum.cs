// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3Spectrum
    {
        /// <summary>Reads the remaining granule bits after scalefactors into transmission order, then discards stuffing.</summary>
        public static int Read(ref Mp3BitReader bits, int sampleRate, Mp3GranuleInfo info, Span<int> values)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(values.Length, Mp3ScaleFactorBands.SpectrumLength);
            if ((uint)info.BigValues > Mp3ScaleFactorBands.SpectrumLength / 2)
            {
                throw new InvalidDataException("The MP3 spectral pair count exceeds a granule.");
            }
            (int first, int second) = GetRegions(sampleRate, info);
            int pairEnd = info.BigValues * 2;
            int position = 0;
            ReadPairs(ref bits, values, ref position, Math.Min(first, pairEnd), info.Table0);
            ReadPairs(ref bits, values, ref position, Math.Min(second, pairEnd), info.Table1);
            ReadPairs(ref bits, values, ref position, pairEnd, info.Table2);
            while (position <= Mp3ScaleFactorBands.SpectrumLength - 4
                && Mp3Huffman.TryReadQuad(ref bits, info.Count1Table, values.Slice(position, 4)))
            {
                position += 4;
            }
            values.Slice(position, Mp3ScaleFactorBands.SpectrumLength - position).Clear();
            bits.Skip(bits.Remaining);
            return position;
        }

        private static (int First, int Second) GetRegions(int sampleRate, Mp3GranuleInfo info)
        {
            switch (info.BlockType)
            {
                case 2:
                    return (Mp3ScaleFactorBands.Short(sampleRate)[3] * 3, Mp3ScaleFactorBands.SpectrumLength);
                case 1:
                case 3:
                    return (Mp3ScaleFactorBands.Long(sampleRate)[8], Mp3ScaleFactorBands.SpectrumLength);
                case 0:
                    if ((uint)info.Region0Count > 15 || (uint)info.Region1Count > 7)
                    {
                        throw new InvalidDataException("The MP3 Huffman region counts are invalid.");
                    }
                    int first = info.Region0Count + 1;
                    int second = first + info.Region1Count + 1;
                    if (second > Mp3ScaleFactorBands.LongBandCount)
                    {
                        throw new InvalidDataException("The MP3 Huffman regions exceed the scalefactor bands.");
                    }
                    ReadOnlySpan<int> bands = Mp3ScaleFactorBands.Long(sampleRate);
                    return (bands[first], bands[second]);
                default:
                    throw new InvalidDataException("The MP3 window type is invalid.");
            }
        }

        private static void ReadPairs(ref Mp3BitReader bits, Span<int> output, ref int position, int end, int table)
        {
            while (position < end)
            {
                (int x, int y) = Mp3Huffman.ReadPair(ref bits, table);
                output[position++] = x;
                output[position++] = y;
            }
        }
    }
}
