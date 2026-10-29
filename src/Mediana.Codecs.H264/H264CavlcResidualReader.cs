// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264CavlcResidualReader(H264Picture target) : IH264ResidualReader
    {
        public int Read(ref H264BitReader bits, H264ResidualCategory category, int plane, int x, int y, scoped Span<int> coefficients)
        {
            if (category == H264ResidualCategory.Luma8x8)
            {
                return ReadLuma8x8(ref bits, x, y, coefficients);
            }
            return H264Cavlc.Read(ref bits, coefficients,
                category == H264ResidualCategory.ChromaDc ? -1 : target.NeighbourCount(plane, x, y), 15);
        }

        private int ReadLuma8x8(ref H264BitReader bits, int x, int y, scoped Span<int> coefficients)
        {
            if (coefficients.Length != 64)
            {
                throw new ArgumentException("CAVLC 8x8 residuals require 64 coefficients.", nameof(coefficients));
            }
            Span<int> block = stackalloc int[16];
            int total = 0;
            for (int index = 0; index < 4; index++)
            {
                int blockX = x + index % 2 * 4;
                int blockY = y + index / 2 * 4;
                int count = H264Cavlc.Read(ref bits, block, target.NeighbourCount(0, blockX, blockY), 15);
                target.Counts(0)[target.BlockIndex(0, blockX, blockY)] = (byte)count;
                total += count;
                for (int position = 0; position < 16; position++)
                {
                    coefficients[position * 4 + index] = block[position];
                }
            }
            return total;
        }
    }
}
