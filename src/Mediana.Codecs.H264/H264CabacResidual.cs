// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Reads frame and field transform coefficients in scan order under H.264 clause 7.3.5.3.3.</summary>
    internal static class H264CabacResidual
    {
        // Rec. ITU-T H.264, Table 9-43 (frame-coded 8x8 significance contexts).
        private static ReadOnlySpan<byte> Significant8x8 =>
            [0, 1, 2, 3, 4, 5, 5, 4, 4, 3, 3, 4, 4, 4, 5, 5,
                4, 4, 4, 4, 3, 3, 6, 7, 7, 7, 8, 9, 10, 9, 8, 7,
                7, 6, 11, 12, 13, 11, 6, 7, 8, 9, 14, 10, 9, 8, 6, 11,
                12, 13, 11, 6, 9, 14, 10, 9, 11, 12, 13, 11, 14, 10, 12];

        // Rec. ITU-T H.264, Table 9-43 (field-coded 8x8 significance contexts).
        private static ReadOnlySpan<byte> Significant8x8Field =>
        [
            0, 1, 1, 2, 2, 3, 3, 4, 5, 6, 7, 7, 7, 8, 4, 5,
            6, 9, 10, 10, 8, 11, 12, 11, 9, 9, 10, 10, 8, 11, 12, 11,
            9, 9, 10, 10, 8, 11, 12, 11, 9, 9, 10, 10, 8, 13, 13, 9,
            9, 10, 10, 8, 13, 13, 9, 9, 10, 10, 14, 14, 14, 14, 14,
        ];

        public static int Read(ref H264BitReader bits, H264CabacReader reader,
            H264ResidualCategory category, int neighbourContext, scoped Span<int> coefficients, int bitDepth = 8, bool field = false)
        {
            (int count, int significantBase, int lastBase, int levelBase) = category switch
            {
                H264ResidualCategory.Intra16LumaDc => (16, 105, 166, 227),
                H264ResidualCategory.Intra16LumaAc => (15, 120, 181, 237),
                H264ResidualCategory.Luma4x4 => (16, 134, 195, 247),
                H264ResidualCategory.ChromaDc when coefficients.Length is 4 or 8 => (coefficients.Length, 149, 210, 257),
                H264ResidualCategory.ChromaAc => (15, 152, 213, 266),
                H264ResidualCategory.Luma8x8 => (64, 402, 417, 426),
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
            if (field)
            {
                int offset = category == H264ResidualCategory.Luma8x8 ? 34 : 172;
                significantBase += offset;
                lastBase += offset;
            }
            ReadOnlySpan<byte> significantPositions = field ? Significant8x8Field : Significant8x8;
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
                int positionContext = category == H264ResidualCategory.ChromaDc ? Math.Min(index / (count / 4), 2) : index;
                int significantContext = significantBase + (count == 64 ? significantPositions[index] : positionContext);
                if (reader.Read(ref bits, significantContext))
                {
                    int lastContext = lastBase + (count == 64 ? Last8x8(index) : positionContext);
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
                        magnitude += ReadExpGolomb(ref bits, reader, bitDepth + 7);
                    }
                }
                bool negative = reader.Bypass(ref bits);
                int signed = negative ? -magnitude : magnitude;
                int magnitudeLimit = 1 << (bitDepth + 7);
                if (signed < -magnitudeLimit || signed >= magnitudeLimit)
                {
                    throw new InvalidDataException("The H.264 CABAC coefficient exceeds the declared transform precision.");
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

        private static int ReadExpGolomb(ref H264BitReader bits, H264CabacReader reader, int maximumOrder)
        {
            int order = 0;
            int value = 0;
            while (reader.Bypass(ref bits))
            {
                if (order >= maximumOrder)
                {
                    throw new InvalidDataException("The H.264 CABAC coefficient suffix exceeds the supported precision.");
                }
                value += 1 << order++;
            }
            return value + (int)reader.Bits(ref bits, order);
        }
    }
}
