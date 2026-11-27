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

        private static ReadOnlySpan<byte> LinearContexts => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14];
        private static ReadOnlySpan<byte> ChromaDc422Contexts => [0, 0, 1, 1, 2, 2, 2];

        // Rec. ITU-T H.264, Table 9-43 (last_significant_coeff_flag for 8x8 blocks).
        private static ReadOnlySpan<byte> Last8x8 =>
        [
            0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4,
            5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8
        ];

        public static int Read(ref H264BitReader bits, H264CabacReader reader,
            H264ResidualCategory category, int neighbourContext, scoped Span<int> coefficients, int bitDepth = 8, bool field = false, int? fullResolutionPlane = null)
        {
            (int count, int significantBase, int lastBase, int levelBase) = FrameContexts(category, coefficients.Length);
            int? codedBase = category == H264ResidualCategory.Luma8x8 ? null : 85 + (int)category * 4;
            if (fullResolutionPlane is int plane)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(plane);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(plane, 2);
                if (category == H264ResidualCategory.Luma8x8)
                {
                    codedBase = 1012 + plane * 4;
                }
                if (plane != 0)
                {
                    int offset = category switch
                    {
                        H264ResidualCategory.Intra16LumaDc => 0,
                        H264ResidualCategory.Intra16LumaAc => 15,
                        H264ResidualCategory.Luma4x4 => 29,
                        H264ResidualCategory.Luma8x8 => 0,
                        _ => throw new ArgumentOutOfRangeException(nameof(category))
                    };
                    if (category == H264ResidualCategory.Luma8x8)
                    {
                        significantBase = (plane == 1 ? 660 : 718) + (field ? 15 : 0);
                        lastBase = (plane == 1 ? 690 : 748) + (field ? 9 : 0);
                        levelBase = plane == 1 ? 708 : 766;
                    }
                    else
                    {
                        codedBase = (plane == 1 ? 460 : 472) + (int)category * 4;
                        significantBase = (plane == 1 ? 484 : 528) + offset + (field ? 292 : 0);
                        lastBase = (plane == 1 ? 572 : 616) + offset + (field ? 292 : 0);
                        levelBase = (plane == 1 ? 952 : 982) + (int)category * 10;
                    }
                }
            }
            if (field && fullResolutionPlane is not (1 or 2))
            {
                int offset = category == H264ResidualCategory.Luma8x8 ? 34 : 172;
                significantBase += offset;
                lastBase += offset;
            }
            ArgumentOutOfRangeException.ThrowIfNegative(neighbourContext);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(neighbourContext, 3);
            if (coefficients.Length != count)
            {
                throw new ArgumentException("The coefficient count does not match the CABAC block category.", nameof(coefficients));
            }
            coefficients.Clear();
            if (codedBase is int codedContext && !reader.Read(ref bits, codedContext + neighbourContext))
            {
                return 0;
            }
            return ReadCoefficients(ref bits, reader, category, count, significantBase, lastBase, levelBase,
                coefficients, bitDepth, field);
        }

        internal static int ReadProgressiveBlock(ref H264BitReader bits, H264CabacReader reader,
            H264ResidualCategory category, int context, scoped Span<int> coefficients)
        {
            if (!reader.Read(ref bits, 85 + (int)category * 4 + context))
            {
                coefficients.Clear();
                return 0;
            }
            (int count, int significantBase, int lastBase, int levelBase) = FrameContexts(category, coefficients.Length);
            coefficients.Clear();
            return ReadCoefficients(ref bits, reader, category, count, significantBase, lastBase, levelBase,
                coefficients, 8, field: false);
        }

        private static (int Count, int Significant, int Last, int Level) FrameContexts(
            H264ResidualCategory category, int coefficientCount) => category switch
            {
                H264ResidualCategory.Intra16LumaDc => (16, 105, 166, 227),
                H264ResidualCategory.Intra16LumaAc => (15, 120, 181, 237),
                H264ResidualCategory.Luma4x4 => (16, 134, 195, 247),
                H264ResidualCategory.ChromaDc when coefficientCount is 4 or 8 => (coefficientCount, 149, 210, 257),
                H264ResidualCategory.ChromaAc => (15, 152, 213, 266),
                H264ResidualCategory.Luma8x8 => (64, 402, 417, 426),
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };

        private static int ReadCoefficients(ref H264BitReader bits, H264CabacReader reader, H264ResidualCategory category,
            int count, int significantBase, int lastBase, int levelBase, scoped Span<int> coefficients, int bitDepth, bool field)
        {
            ReadOnlySpan<byte> significance = count switch
            {
                64 => field ? Significant8x8Field : Significant8x8,
                8 => ChromaDc422Contexts,
                _ => LinearContexts
            };
            ReadOnlySpan<byte> lastSignificance = count == 64 ? Last8x8 : significance;
            Span<byte> positions = stackalloc byte[64];
            int total = 0;
            for (int index = 0; index < count - 1; index++)
            {
                if (reader.Read(ref bits, significantBase + significance[index]))
                {
                    positions[total++] = (byte)index;
                    if (reader.Read(ref bits, lastBase + lastSignificance[index]))
                    {
                        return ReadLevels(ref bits, reader, category, levelBase, positions[..total], coefficients, bitDepth);
                    }
                }
            }
            positions[total++] = (byte)(count - 1);
            return ReadLevels(ref bits, reader, category, levelBase, positions[..total], coefficients, bitDepth);
        }

        private static int ReadLevels(ref H264BitReader bits, H264CabacReader reader, H264ResidualCategory category,
            int levelBase, scoped ReadOnlySpan<byte> positions, scoped Span<int> coefficients, int bitDepth)
        {
            int equalOne = 0;
            int greaterOne = 0;
            for (int index = positions.Length - 1; index >= 0; index--)
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
            return positions.Length;
        }

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
