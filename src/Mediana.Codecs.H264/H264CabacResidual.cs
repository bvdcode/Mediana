// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Reads progressive 4:2:0 transform coefficients in scan order under H.264 clause 7.3.5.3.3.</summary>
    internal static class H264CabacResidual
    {
        // Rec. ITU-T H.264, Table 9-43 (frame-coded 8x8 significance contexts).
        private static ReadOnlySpan<byte> Significant8x8 =>
            [0, 1, 2, 3, 4, 5, 5, 4, 4, 3, 3, 4, 4, 4, 5, 5,
                4, 4, 4, 4, 3, 3, 6, 7, 7, 7, 8, 9, 10, 9, 8, 7,
                7, 6, 11, 12, 13, 11, 6, 7, 8, 9, 14, 10, 9, 8, 6, 11,
                12, 13, 11, 6, 9, 14, 10, 9, 11, 12, 13, 11, 14, 10, 12];

        public static int Read(ref H264BitReader bits, H264CabacReader reader,
            H264ResidualCategory category, int neighbourContext, scoped Span<int> coefficients)
        {
            (int count, int significantBase, int lastBase, int levelBase) = category switch
            {
                H264ResidualCategory.Intra16LumaDc => (16, 105, 166, 227),
                H264ResidualCategory.Intra16LumaAc => (15, 120, 181, 237),
                H264ResidualCategory.Luma4x4 => (16, 134, 195, 247),
                H264ResidualCategory.ChromaDc => (4, 149, 210, 257),
                H264ResidualCategory.ChromaAc => (15, 152, 213, 266),
                H264ResidualCategory.Luma8x8 => (64, 402, 417, 426),
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
            ArgumentOutOfRangeException.ThrowIfNegative(neighbourContext);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(neighbourContext, 3);
            if (coefficients.Length != count)
            {
                throw new ArgumentException("The coefficient count does not match the CABAC block category.", nameof(coefficients));
            }
            coefficients.Clear();
            if (category != H264ResidualCategory.Luma8x8 && !reader.Read(ref bits, 85 + (int)category * 4 + neighbourContext))
            {
                return 0;
            }
            Span<int> positions = stackalloc int[64];
            int total = 0;
            int last = count - 1;
            for (int index = 0; index < count - 1; index++)
            {
                int significantContext = significantBase + (count == 64 ? Significant8x8[index] : index);
                if (reader.Read(ref bits, significantContext))
                {
                    int lastContext = lastBase + (count == 64 ? Last8x8(index) : index);
                    if (reader.Read(ref bits, lastContext))
                    {
                        last = index;
                        break;
                    }
                    positions[total++] = index;
                }
            }
            positions[total++] = last;
            int equalOne = 0;
            int greaterOne = 0;
            for (int index = total - 1; index >= 0; index--)
            {
                int firstContext = greaterOne == 0 ? Math.Min(4, 1 + equalOne) : 0;
                int magnitude = 1;
                if (reader.Read(ref bits, levelBase + firstContext))
                {
                    int continuation = levelBase + 5 + Math.Min(category == H264ResidualCategory.ChromaDc ? 3 : 4, greaterOne);
                    int prefix = 1;
                    while (prefix < 14 && reader.Read(ref bits, continuation))
                    {
                        prefix++;
                    }
                    magnitude = prefix + 1;
                    if (prefix == 14)
                    {
                        magnitude += ReadExpGolomb(ref bits, reader);
                    }
                }
                bool negative = reader.Bypass(ref bits);
                int signed = negative ? -magnitude : magnitude;
                if (signed is < short.MinValue or > short.MaxValue)
                {
                    throw new InvalidDataException("The H.264 CABAC coefficient exceeds the 8-bit transform precision.");
                }
                coefficients[positions[index]] = signed;
                if (magnitude == 1)
                {
                    equalOne++;
                }
                else
                {
                    greaterOne++;
                }
            }
            return total;
        }

        private static int Last8x8(int index) => index switch
        {
            0 => 0,
            >= 1 and <= 15 => 1,
            >= 16 and <= 31 => 2,
            >= 32 and <= 39 => 3,
            >= 40 and <= 47 => 4,
            >= 48 and <= 51 => 5,
            >= 52 and <= 55 => 6,
            >= 56 and <= 59 => 7,
            >= 60 and <= 62 => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        private static int ReadExpGolomb(ref H264BitReader bits, H264CabacReader reader)
        {
            int order = 0;
            int value = 0;
            while (reader.Bypass(ref bits))
            {
                if (order >= 15)
                {
                    throw new InvalidDataException("The H.264 CABAC coefficient suffix exceeds the supported precision.");
                }
                value += 1 << order++;
            }
            return value + (int)reader.Bits(ref bits, order);
        }
    }
}
