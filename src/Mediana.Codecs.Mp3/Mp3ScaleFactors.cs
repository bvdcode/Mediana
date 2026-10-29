// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3ScaleFactors
    {
        public const int Capacity = 39;

        private static ReadOnlySpan<byte> FirstLengths => [0, 0, 0, 0, 3, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4];
        private static ReadOnlySpan<byte> SecondLengths => [0, 1, 2, 3, 0, 1, 2, 3, 1, 2, 3, 1, 2, 3, 2, 3];
        private static ReadOnlySpan<byte> LongGroups => [0, 6, 11, 16, 21];

        public static void ReadMpeg1(ref Mp3BitReader bits, Mp3GranuleInfo info, int reuseMask, Span<byte> values)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(reuseMask);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(reuseMask, 15);
            Validate(values, info, 15);
            int first = FirstLengths[info.ScaleFactorCompression];
            int second = SecondLengths[info.ScaleFactorCompression];
            if (info.BlockType == 2)
            {
                if (reuseMask != 0)
                {
                    throw new InvalidDataException("MP3 short blocks cannot reuse previous scalefactors.");
                }
                int count = info.MixedBlock ? 17 : 18;
                ReadValues(ref bits, values[..count], first);
                ReadValues(ref bits, values.Slice(count, 18), second);
                values[(count + 18)..Capacity].Clear();
                return;
            }
            for (int group = 0; group < 4; group++)
            {
                if ((reuseMask & (8 >> group)) == 0)
                {
                    int start = LongGroups[group];
                    int count = LongGroups[group + 1] - start;
                    ReadValues(ref bits, values.Slice(start, count), group < 2 ? first : second);
                }
            }
            values[21..Capacity].Clear();
        }

        public static void ReadLowRate(ref Mp3BitReader bits, Mp3GranuleInfo info, bool intensityChannel,
            Span<byte> values, Span<byte> intensityLimits)
        {
            Validate(values, info, 511);
            ArgumentOutOfRangeException.ThrowIfLessThan(intensityLimits.Length, Capacity);
            Span<int> lengths = stackalloc int[4];
            int family = GetLowRateLengths(info.ScaleFactorCompression, intensityChannel, lengths);
            ReadOnlySpan<byte> counts = GetLowRateCounts(family, info.BlockType == 2, info.MixedBlock);
            int start = 0;
            intensityLimits[..Capacity].Fill(byte.MaxValue);
            for (int partition = 0; partition < 4; partition++)
            {
                int count = counts[partition];
                int width = lengths[partition];
                ReadValues(ref bits, values.Slice(start, count), width);
                if (intensityChannel && width > 0)
                {
                    intensityLimits.Slice(start, count).Fill((byte)((1 << width) - 1));
                }
                start += count;
            }
            values[start..Capacity].Clear();
        }

        private static int GetLowRateLengths(int compression, bool intensity, Span<int> lengths)
        {
            lengths.Clear();
            if (intensity)
            {
                int value = compression >> 1;
                if (value < 180)
                {
                    lengths[0] = value / 36;
                    lengths[1] = value % 36 / 6;
                    lengths[2] = value % 6;
                    return 3;
                }
                if (value < 244)
                {
                    value -= 180;
                    lengths[0] = value >> 4;
                    lengths[1] = (value >> 2) & 3;
                    lengths[2] = value & 3;
                    return 4;
                }
                value -= 244;
                lengths[0] = value / 3;
                lengths[1] = value % 3;
                return 5;
            }
            if (compression < 400)
            {
                lengths[0] = (compression >> 4) / 5;
                lengths[1] = (compression >> 4) % 5;
                lengths[2] = (compression >> 2) & 3;
                lengths[3] = compression & 3;
                return 0;
            }
            if (compression < 500)
            {
                int value = compression - 400;
                lengths[0] = (value >> 2) / 5;
                lengths[1] = (value >> 2) % 5;
                lengths[2] = value & 3;
                return 1;
            }
            lengths[0] = (compression - 500) / 3;
            lengths[1] = (compression - 500) % 3;
            return 2;
        }

        private static ReadOnlySpan<byte> GetLowRateCounts(int family, bool shortBlock, bool mixed) => (family, shortBlock, mixed) switch
        {
            (0, false, _) => [6, 5, 5, 5],
            (1, false, _) => [6, 5, 7, 3],
            (2, false, _) => [11, 10, 0, 0],
            (3, false, _) => [7, 7, 7, 0],
            (4, false, _) => [6, 6, 6, 3],
            (5, false, _) => [8, 8, 5, 0],
            (0, true, false) => [9, 9, 9, 9],
            (1, true, false) => [9, 9, 12, 6],
            (2, true, false) => [18, 18, 0, 0],
            (3, true, false) => [12, 12, 12, 0],
            (4, true, false) => [12, 9, 9, 6],
            (5, true, false) => [15, 12, 9, 0],
            (0, true, true) => [6, 9, 9, 9],
            (1, true, true) => [6, 9, 12, 6],
            (2, true, true) => [15, 18, 0, 0],
            (3, true, true) => [6, 15, 12, 0],
            (4, true, true) => [6, 12, 9, 6],
            (5, true, true) => [6, 18, 9, 0],
            _ => throw new InvalidOperationException("The MP3 scalefactor partition is invalid.")
        };

        private static void Validate(Span<byte> values, Mp3GranuleInfo info, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(values.Length, Capacity);
            ArgumentOutOfRangeException.ThrowIfNegative(info.ScaleFactorCompression);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(info.ScaleFactorCompression, maximum);
        }

        private static void ReadValues(ref Mp3BitReader bits, Span<byte> values, int width)
        {
            if (width == 0)
            {
                values.Clear();
                return;
            }
            if (values.Length * width > bits.Remaining)
            {
                throw new InvalidDataException("The MP3 scalefactors exceed the declared granule data.");
            }
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = (byte)bits.Read(width);
            }
        }
    }
}
