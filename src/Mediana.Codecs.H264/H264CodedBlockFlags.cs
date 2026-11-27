// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264CodedBlockFlags
    {
        public const uint Luma = 0xFFFF;
        public const uint All = 0xFFFFFF;

        private const uint FirstLumaColumn = 0x1111;
        private const uint FirstLumaRow = 0x000F;
        private const uint FirstChromaColumns = 0x550000;
        private const uint FirstChromaRows = 0x330000;

        public static bool Supports(H264SequenceParameterSet sequence) => sequence.FrameMbsOnly &&
            sequence.ChromaFormat == 1 && sequence.LumaBitDepth == 8 && sequence.ChromaBitDepth == 8;

        public static int LumaIndex(int x, int y) => (y & 12) + ((x >> 2) & 3);

        public static int BlockIndex(int plane, int x, int y) => plane == 0
            ? LumaIndex(x, y)
            : 12 + plane * 4 + ((y >> 1) & 2) + ((x >> 2) & 1);

        public static uint LeftBorder(uint neighbour) =>
            ((neighbour >> 3) & FirstLumaColumn) | ((neighbour >> 1) & FirstChromaColumns);

        public static uint TopBorder(uint neighbour) =>
            ((neighbour >> 12) & FirstLumaRow) | ((neighbour >> 2) & FirstChromaRows);

        public static uint InteriorLeft(uint flags) => (flags << 1) & (All ^ (FirstLumaColumn | FirstChromaColumns));

        public static uint InteriorTop(uint flags) =>
            (((flags & Luma) << 4) & (Luma ^ FirstLumaRow)) |
            (((flags & (All ^ Luma)) << 2) & (All ^ (Luma | FirstChromaRows)));
    }
}
