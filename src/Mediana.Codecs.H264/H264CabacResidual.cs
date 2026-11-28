// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.H264
{
    /// <summary>Reads frame and field transform coefficients in scan order under H.264 clause 7.3.5.3.3.</summary>
    internal static partial class H264CabacResidual
    {
        private const int Frame8x8SignificantBase = 402;
        private const int Frame8x8LastBase = 417;
        private const int Frame8x8LevelBase = 426;

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
            (int count, int significantBase, int lastBase, int levelBase, int continuationLimit) = FrameContexts(category, coefficients.Length);
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
            return ReadCoefficients(ref bits, reader, count, significantBase, lastBase, levelBase, continuationLimit,
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
            (int count, int significantBase, int lastBase, int levelBase, int continuationLimit) = FrameContexts(category, coefficients.Length);
            coefficients.Clear();
            return ReadCoefficients(ref bits, reader, count, significantBase, lastBase, levelBase, continuationLimit,
                coefficients, 8, field: false);
        }

        private static (int Count, int Significant, int Last, int Level, int ContinuationLimit) FrameContexts(
            H264ResidualCategory category, int coefficientCount) => category switch
            {
                H264ResidualCategory.Intra16LumaDc => (16, 105, 166, 227, 4),
                H264ResidualCategory.Intra16LumaAc => (15, 120, 181, 237, 4),
                H264ResidualCategory.Luma4x4 => (16, 134, 195, 247, 4),
                H264ResidualCategory.ChromaDc when coefficientCount is 4 or 8 => (coefficientCount, 149, 210, 257, 3),
                H264ResidualCategory.ChromaAc => (15, 152, 213, 266, 4),
                H264ResidualCategory.Luma8x8 => (64, Frame8x8SignificantBase, Frame8x8LastBase, Frame8x8LevelBase, 4),
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };

        private static int ReadCoefficients(ref H264BitReader bits, H264CabacReader reader, int count, int significantBase,
            int lastBase, int levelBase, int continuationLimit, scoped Span<int> coefficients, int bitDepth, bool field)
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
            int index = 0;
            for (; index < count - 1; index++)
            {
                if (reader.Read(ref bits, significantBase + significance[index]))
                {
                    positions[total++] = (byte)index;
                    if (reader.Read(ref bits, lastBase + lastSignificance[index]))
                    {
                        break;
                    }
                }
            }
            if (index == count - 1)
            {
                positions[total++] = (byte)index;
            }
            return ReadLevels(ref bits, reader, levelBase, continuationLimit, positions[..total], coefficients, bitDepth);
        }

        private static int ReadLevels(ref H264BitReader bits, H264CabacReader reader, int levelBase, int continuationLimit,
            scoped ReadOnlySpan<byte> positions, scoped Span<int> coefficients, int bitDepth)
        {
            int equalOne = 0;
            int greaterOne = 0;
            int coefficientBits = bitDepth + 7;
            for (int index = positions.Length - 1; index >= 0; index--)
            {
                coefficients[positions[index]] = ReadLevel(ref bits, reader, levelBase, continuationLimit,
                    coefficientBits, ref equalOne, ref greaterOne);
            }
            return positions.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadLevel(ref H264BitReader bits, H264CabacReader reader, int levelBase, int continuationLimit,
            int coefficientBits, ref int equalOne, ref int greaterOne)
        {
            int firstContext = greaterOne == 0 ? Math.Min(4, 1 + equalOne) : 0;
            int magnitude = 1;
            if (reader.Read(ref bits, levelBase + firstContext))
            {
                int continuation = levelBase + 5 + Math.Min(continuationLimit, greaterOne);
                int prefix = 1;
                while (prefix < 14 && reader.Read(ref bits, continuation))
                {
                    prefix++;
                }
                magnitude = prefix + 1;
                if (prefix == 14)
                {
                    magnitude += ReadExpGolomb(ref bits, reader, coefficientBits);
                }
            }
            int signed = reader.Bypass(ref bits) ? -magnitude : magnitude;
            int magnitudeLimit = 1 << coefficientBits;
            if (signed < -magnitudeLimit || signed >= magnitudeLimit)
            {
                throw new InvalidDataException("The H.264 CABAC coefficient exceeds the declared transform precision.");
            }
            if (magnitude == 1)
            {
                equalOne++;
            }
            else
            {
                greaterOne++;
            }
            return signed;
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
