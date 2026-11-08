// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264CabacSyntax<TWriter>
    {
        public void WriteMacroblock<TLimit>(int x, int y, int pattern, ReadOnlySpan<int> coefficients,
            int intraMode, int differenceX, int differenceY, int neighbourX, int neighbourY,
            TLimit limit) where TLimit : struct, IH264CabacCostLimit<TWriter>
        {
            int address = y / 16 * target.Sequence.WidthInMbs + x / 16;
            WriteSkip(address, false);
            if (intraMode >= 0)
            {
                WriteIntra16Type(address, intraMode, pattern);
                WriteChromaMode(address);
                WriteQuantizerDelta(0);
                TryWriteResidual(H264ResidualCategory.Intra16LumaDc, 0, x, y,
                    coefficients.Slice(240, 16), out _);
            }
            else
            {
                WritePredictiveType();
                WriteMotionDifference(differenceX, false, neighbourX);
                WriteMotionDifference(differenceY, true, neighbourY);
                WritePattern(address, pattern);
                if (pattern != 0) { WriteQuantizerDelta(0); }
            }
            if (limit.Reached(writer)) { return; }
            for (int index = 0; index < 16; index++)
            {
                (int bx, int by) = H264Picture.LumaBlockPosition(index);
                int count = 0;
                if ((pattern & (1 << (index / 4))) != 0)
                {
                    H264ResidualCategory category = intraMode >= 0
                        ? H264ResidualCategory.Intra16LumaAc : H264ResidualCategory.Luma4x4;
                    int length = intraMode >= 0 ? 15 : 16;
                    TryWriteResidual(category, 0, x + bx, y + by,
                        coefficients.Slice(index * length, length), out count);
                }
                target.Counts(0)[target.BlockIndex(0, x + bx, y + by)] = (byte)count;
                if (limit.Reached(writer)) { return; }
            }
            int chroma = pattern >> 4;
            if (chroma != 0)
            {
                TryWriteResidual(H264ResidualCategory.ChromaDc, 1, x / 2, y / 2,
                    coefficients.Slice(376, 4), out _);
                TryWriteResidual(H264ResidualCategory.ChromaDc, 2, x / 2, y / 2,
                    coefficients.Slice(380, 4), out _);
            }
            if (limit.Reached(writer)) { return; }
            for (int plane = 1; plane <= 2; plane++)
                for (int block = 0; block < 4; block++)
                {
                    int bx = block % 2 * 4, by = block / 2 * 4, count = 0;
                    if (chroma == 2)
                    {
                        TryWriteResidual(H264ResidualCategory.ChromaAc, plane, x / 2 + bx, y / 2 + by,
                            coefficients.Slice(256 + ((plane - 1) * 4 + block) * 15, 15), out count);
                    }
                    target.Counts(plane)[target.BlockIndex(plane, x / 2 + bx, y / 2 + by)] = (byte)count;
                    if (limit.Reached(writer)) { return; }
                }
        }
    }
}
