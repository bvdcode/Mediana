// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.H264
{
    internal static partial class H264CabacResidual
    {
        internal static void ReadPreparedLuma8x8(ref H264BitReader bits, H264CabacReader reader, int qp,
            scoped Span<int> coefficients, out H264ResidualSymmetry symmetry, out int dcLevel)
        {
            ulong positions = ReadFrame8x8Positions(ref bits, reader);
            const int coefficientBits = 15;
            int equalOne = 0;
            int greaterOne = 0;
            if (positions == 1)
            {
                symmetry = H264ResidualSymmetry.Both;
                dcLevel = ReadLevel(ref bits, reader, Frame8x8LevelBase, 4, coefficientBits, ref equalOne, ref greaterOne);
                return;
            }

            dcLevel = 0;
            symmetry = H264VectorInverse8x8.GetSymmetry(positions);
            coefficients[..H264VectorInverse8x8.PreparedLength(symmetry)].Clear();
            ReadOnlySpan<int> layout = H264VectorInverse8x8.PreparedLayout(qp, symmetry);
            int rightShift = Math.Max(2 - qp / 6, 0);
            int rounding = (1 << rightShift) >> 1;
            do
            {
                int index = 63 - BitOperations.LeadingZeroCount(positions);
                positions ^= 1UL << index;
                int level = ReadLevel(ref bits, reader, Frame8x8LevelBase, 4, coefficientBits, ref equalOne, ref greaterOne);
                int entry = layout[index];
                int indexInBlock = entry & H264VectorInverse8x8.PreparedIndexMask;
                int scale = entry >> H264VectorInverse8x8.PreparedIndexBits;
                coefficients[indexInBlock] = (level * scale + rounding) >> rightShift;
            }
            while (positions != 0);
        }

        private static ulong ReadFrame8x8Positions(ref H264BitReader bits, H264CabacReader reader)
        {
            ReadOnlySpan<byte> significance = Significant8x8;
            ReadOnlySpan<byte> lastSignificance = Last8x8;
            ulong positions = 0;
            for (int index = 0; index < 63; index++)
            {
                if (reader.Read(ref bits, Frame8x8SignificantBase + significance[index]))
                {
                    positions |= 1UL << index;
                    if (reader.Read(ref bits, Frame8x8LastBase + lastSignificance[index]))
                    {
                        return positions;
                    }
                }
            }
            return positions | (1UL << 63);
        }
    }
}
