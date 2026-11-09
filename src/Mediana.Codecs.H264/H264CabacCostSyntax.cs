// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264CabacCostSyntax(H264CabacMacroblockState state, H264Picture target, int quantizer)
        : H264CabacSyntax<H264CabacCostWriter>(new(), state, target, H264SliceKind.P, quantizer)
    {
        public void Reset(int range, ReadOnlySpan<H264CabacContext> initialContexts, int previousDelta)
        {
            writer.Reset(range);
            RestoreContexts(initialContexts);
            PreviousDelta = previousDelta;
        }

        public double Estimate<TLimit>(int x, int y, H264PreparedMacroblock candidate, H264MacroblockMotion motion,
            TLimit limit) where TLimit : struct, IH264CabacCostLimit<H264CabacCostWriter>
        {
            WriteMacroblock(x, y, candidate.Pattern, candidate.Coefficients, candidate.IntraMode,
                motion.DifferenceX, motion.DifferenceY, motion.NeighbourX, motion.NeighbourY, limit);
            return writer.Cost;
        }
    }
}
