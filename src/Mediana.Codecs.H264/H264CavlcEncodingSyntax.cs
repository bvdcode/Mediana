// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264CavlcEncodingSyntax(H264BitWriter bits, H264Picture target) : IH264EncodingSyntax
    {
        public void WriteIntraMode(int mode, int predicted)
        {
            bits.WriteFlag(mode == predicted);
            if (mode != predicted) { bits.WriteBits((uint)(mode < predicted ? mode : mode - 1), 3); }
        }

        public bool TryWriteResidual(H264ResidualCategory category, int plane, int x, int y,
            ReadOnlySpan<int> coefficients, out int count)
        {
            int neighbours = category switch
            {
                H264ResidualCategory.ChromaDc => -1,
                H264ResidualCategory.Intra16LumaDc or H264ResidualCategory.Intra16LumaAc or
                    H264ResidualCategory.Luma4x4 or H264ResidualCategory.ChromaAc => target.NeighbourCount(plane, x, y),
                H264ResidualCategory.Luma8x8 => throw new NotSupportedException("CAVLC encoding requires interleaved 4x4 coefficient lists."),
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
            return H264CavlcWriter.TryWrite(bits, coefficients, neighbours, out count);
        }
    }
}
