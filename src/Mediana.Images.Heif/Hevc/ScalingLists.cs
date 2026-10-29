// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Owns immutable scaling matrices for one parameter set (HEVC sections 7.3.4 and 7.4.5).
    /// </summary>
    /// <remarks>
    /// Matrix coordinates are (x,y). Lists arrive in diagonal order and are stored row-major.
    /// The 16/32 matrices expand each base entry to a 2/4-square region, with a separate DC
    /// value at (0,0). Sharing a parsed instance between workers is safe.
    /// </remarks>
    internal class ScalingLists
    {
        private static readonly byte[] DefaultIntra =
        {
            16,16,16,16,16,16,16,16,16,16,17,16,17,16,17,18,
            17,18,18,17,18,21,19,20,21,20,19,21,24,22,22,24,
            24,22,22,24,25,25,27,30,27,25,25,29,31,35,35,31,
            29,36,41,44,41,36,47,54,54,47,65,70,65,88,88,115
        };
        private static readonly byte[] DefaultInter =
        {
            16,16,16,16,16,16,16,16,16,16,17,17,17,17,17,18,
            18,18,18,18,18,20,20,20,20,20,20,20,24,24,24,24,
            24,24,24,24,25,25,25,25,25,25,25,28,28,28,28,28,
            28,33,33,33,33,33,41,41,41,41,54,54,54,71,71,91
        };

        private readonly byte[][] matrices = new byte[24][];
        private readonly byte[] dc = new byte[24];
        private readonly bool enabled;

        private ScalingLists(bool enabled)
        {
            this.enabled = enabled;
            if (!enabled)
            {
                return;
            }
            for (int sizeId = 0; sizeId < 4; sizeId++)
            {
                int step = sizeId == 3 ? 3 : 1;
                for (int matrixId = 0; matrixId < 6; matrixId += step)
                {
                    SetDefault(sizeId, matrixId);
                }
            }
        }

        public bool IsFlat => !enabled;

        public static ScalingLists Flat { get; } = new ScalingLists(false);
        public static ScalingLists Default { get; } = new ScalingLists(true);

        public int Factor(int log2Size, int matrixId, int x, int y)
        {
            ValidateCoordinates(log2Size, matrixId, x, y);

            if (!enabled)
            {
                return 16;
            }
            int listSize = (log2Size == 5) && ((matrixId % 3) != 0) ? 4 : log2Size;
            int index = ((listSize - 2) * 6) + matrixId;
            if ((log2Size > 3) && (x == 0) && (y == 0))
            {
                return dc[index];
            }
            int shift = Math.Max(0, log2Size - 3);
            int width = log2Size == 2 ? 4 : 8;
            return matrices[index][((y >> shift) * width) + (x >> shift)];
        }

        public static ScalingLists Read(BitReader reader)
        {
            ScalingLists lists = new ScalingLists(true);
            for (int sizeId = 0; sizeId < 4; sizeId++)
            {
                int step = sizeId == 3 ? 3 : 1;
                for (int matrixId = 0; matrixId < 6; matrixId += step)
                {
                    int index = (sizeId * 6) + matrixId;
                    if (!reader.ReadFlag())
                    {
                        uint delta = reader.ReadUev();
                        if (delta > (uint)(matrixId / step))
                        {
                            throw new InvalidDataException("Invalid scaling matrix prediction delta.");
                        }
                        if (delta != 0)
                        {
                            int previous = index - ((int)delta * step);
                            lists.matrices[index] = lists.matrices[previous];
                            lists.dc[index] = lists.dc[previous];
                        }
                    }
                    else
                    {
                        lists.ReadExplicit(reader, sizeId, index);
                    }
                }
            }

            return lists;
        }

        private void SetDefault(int sizeId, int matrixId)
        {
            int index = (sizeId * 6) + matrixId;
            dc[index] = 16;
            int size = sizeId == 0 ? 4 : 8;
            byte[] values = new byte[size * size];
            ReadOnlySpan<int> scan = CoefficientScan.Matrix(size);
            for (int i = 0; i < values.Length; i++)
            {
                values[scan[i]] = sizeId == 0 ? (byte)16 : (matrixId < 3 ? DefaultIntra[i] : DefaultInter[i]);
            }

            matrices[index] = values;
        }

        private void ReadExplicit(BitReader reader, int sizeId, int index)
        {
            int next = 8;
            if (sizeId > 1)
            {
                int dcMinus8 = reader.ReadSev();
                if ((dcMinus8 < -7) || (dcMinus8 > 247))
                {
                    throw new InvalidDataException("Invalid scaling matrix DC value.");
                }
                next = dcMinus8 + 8;
                dc[index] = (byte)next;
            }

            int size = sizeId == 0 ? 4 : 8;
            byte[] values = new byte[size * size];
            ReadOnlySpan<int> scan = CoefficientScan.Matrix(size);
            for (int i = 0; i < values.Length; i++)
            {
                int delta = reader.ReadSev();
                if ((delta < -128) || (delta > 127))
                {
                    throw new InvalidDataException("Invalid scaling matrix coefficient delta.");
                }
                next = (next + delta + 256) % 256;
                values[scan[i]] = (byte)next;
            }

            matrices[index] = values;
        }

        private static void ValidateCoordinates(int log2Size, int matrixId, int x, int y)
        {
            if ((log2Size < 2) || (log2Size > 5) || (matrixId < 0) || (matrixId > 5) ||
                (x < 0) || (y < 0) || (x >= (1 << log2Size)) || (y >= (1 << log2Size)))
            {
                throw new ArgumentOutOfRangeException(nameof(log2Size));
            }
        }
    }
}
