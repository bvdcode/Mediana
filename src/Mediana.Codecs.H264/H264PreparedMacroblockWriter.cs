// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264PreparedMacroblockWriter
    {
        public static void Write(H264CabacEncodingSyntax syntax, H264Picture target, H264CabacPredictionWindow motion,
            int x, int y, H264PreparedMacroblock candidate, H264MacroblockMotion vector)
        {
            syntax.WriteMacroblock(x, y, candidate.Pattern, candidate.Coefficients, candidate.IntraMode,
                vector.DifferenceX, vector.DifferenceY, vector.NeighbourX, vector.NeighbourY, new H264CabacUnlimitedCost<H264CabacOutputWriter>());
            if (candidate.IsIntra)
            {
                target.SetMotion(H264ReferenceList.L0, x, y, 16, 16, -1, 0, 0);
                for (int row = 0; row < 16; row += 4)
                {
                    for (int column = 0; column < 16; column += 4) { target.SetLumaMode(x + column, y + row, 2); }
                }
            }
            else
            {
                target.SetMotion(H264ReferenceList.L0, x, y, 16, 16, 0, vector.X, vector.Y);
                for (int row = 0; row < 16; row += 4)
                {
                    motion.MotionX(H264ReferenceList.L0).Slice(motion.BlockIndex(x, y + row), 4).Fill((short)vector.DifferenceX);
                    motion.MotionY(H264ReferenceList.L0).Slice(motion.BlockIndex(x, y + row), 4).Fill((short)vector.DifferenceY);
                }
            }
            target.WritePreparedMacroblock(x, y, candidate.Pixels);
        }
    }
}
