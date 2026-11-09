// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264CabacMacroblockSnapshot(byte Type, byte Pattern, byte Chroma, byte Dc)
    {
        public static H264CabacMacroblockSnapshot Capture(H264CabacMacroblockState state, H264Picture target,
            int x, int y, Span<byte> counts)
        {
            CopyCounts(target, x, y, counts, false);
            int address = y / 16 * target.Sequence.WidthInMbs + x / 16;
            return new(state.Types[address], state.Patterns[address], state.ChromaModes[address], state.DcFlags[address]);
        }

        public void Restore(H264CabacMacroblockState state, H264Picture target, int x, int y, Span<byte> counts)
        {
            CopyCounts(target, x, y, counts, true);
            int address = y / 16 * target.Sequence.WidthInMbs + x / 16;
            state.Types[address] = Type;
            state.Patterns[address] = Pattern;
            state.ChromaModes[address] = Chroma;
            state.DcFlags[address] = Dc;
        }

        private static void CopyCounts(H264Picture target, int x, int y, Span<byte> counts, bool restore)
        {
            int offset = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8, scale = plane == 0 ? 1 : 2;
                for (int row = 0; row < size; row += 4)
                {
                    for (int column = 0; column < size; column += 4)
                    {
                        int index = target.BlockIndex(plane, x / scale + column, y / scale + row);
                        if (restore) { target.Counts(plane)[index] = counts[offset++]; }
                        else { counts[offset++] = target.Counts(plane)[index]; }
                    }
                }
            }
        }
    }
}
