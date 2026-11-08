// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3.Tests
{
    internal static class Ac3SyntheticFrame
    {
        public static byte[] EnhancedCouplingPrefix()
        {
            Ac3TestBitWriter writer = new(2048);
            writer.Write(0xb77, 16); writer.Write(0, 2); writer.Write(0, 3); writer.Write(1023, 11);
            writer.Write(0, 2); writer.Write(3, 2); writer.Write(2, 3); writer.Write(0, 1); writer.Write(16, 5);
            writer.Write(31, 5); writer.Write(0, 4);
            writer.Write(1, 1); writer.Write(0, 1); writer.Write(0, 2); writer.Write(0, 1);
            writer.Write(1, 1); writer.Write(1, 1); writer.Write(0, 5);
            writer.Write(1, 1); writer.Write(0, 5);
            for (int block = 0; block < 6; block++)
            {
                writer.Write(block == 0 ? 1 : 0, 2); writer.Write(block == 0 ? 1 : 0, 2); writer.Write(block == 0 ? 1 : 0, 2);
            }
            writer.Write(0, 10); writer.Write(63, 6); writer.Write(15, 4); writer.Write(0, 1);
            writer.Write(0, 2); writer.Write(0, 2); writer.Write(0, 1); writer.Write(0, 1);
            writer.Write(1, 1);
            return writer.Finish(true);
        }

        public static byte[] Create(bool enhanced, bool switched, int blocks = 6, int? dynamicRange = null, int exponentStrategy = 1)
        {
            int length = enhanced ? 2048 : 2560;
            Ac3TestBitWriter writer = new(length);
            writer.Write(0xb77, 16);
            if (enhanced) { WriteEnhancedHeader(writer, length, blocks, exponentStrategy); }
            else
            {
                writer.Write(0, 16); writer.Write(0, 2); writer.Write(36, 6); writer.Write(8, 5); writer.Write(0, 3);
                writer.Write(1, 3); writer.Write(0, 1); writer.Write(31, 5);
                writer.Write(0, 3); writer.Write(0, 2); writer.Write(0, 3);
            }
            for (int block = 0; block < blocks; block++)
            {
                writer.Write(switched && block % 2 == 1 ? 1 : 0, 1); writer.Write(0, 1);
                writer.Write(block == 0 && dynamicRange is not null ? 1 : 0, 1);
                if (block == 0 && dynamicRange is not null) { writer.Write(dynamicRange.Value, 8); }
                if (enhanced)
                {
                    if (block > 0) { writer.Write(1, 1); }
                    writer.Write(0, 1);
                }
                else
                {
                    writer.Write(block == 0 ? 1 : 0, 1);
                    if (block == 0) { writer.Write(0, 1); }
                    writer.Write(block == 0 ? exponentStrategy : 0, 2);
                }
                if (block == 0)
                {
                    writer.Write(0, 6); writer.Write(0, 4);
                    for (int group = 0; group < 24 >> (exponentStrategy - 1); group++) { writer.Write(62, 7); }
                    writer.Write(0, 2);
                }
                if (!enhanced)
                {
                    writer.Write(block == 0 ? 1 : 0, 1);
                    if (block == 0) { writer.Write(2, 2); writer.Write(1, 2); writer.Write(1, 2); writer.Write(2, 2); writer.Write(7, 3); }
                    writer.Write(block == 0 ? 1 : 0, 1);
                    if (block == 0) { writer.Write(63, 6); writer.Write(15, 4); writer.Write(4, 3); }
                    writer.Write(0, 2);
                }
                else { writer.Write(0, 1); }
                for (int bin = 0; bin < 73; bin++) { writer.Write(bin switch { 3 => 33, 19 => -17, _ => 0 }, 16); }
            }
            return writer.Finish(enhanced);
        }

        private static void WriteEnhancedHeader(Ac3TestBitWriter writer, int length, int blocks, int exponentStrategy)
        {
            writer.Write(0, 2); writer.Write(0, 3); writer.Write(length / 2 - 1, 11); writer.Write(0, 2);
            writer.Write(blocks switch { 1 => 0, 2 => 1, 3 => 2, 6 => 3, _ => throw new ArgumentOutOfRangeException(nameof(blocks)) }, 2);
            writer.Write(1, 3); writer.Write(0, 1); writer.Write(16, 5); writer.Write(31, 5); writer.Write(0, 3);
            if (blocks != 6) { writer.Write(0, 1); }
            writer.Write(0, 1);
            if (blocks == 6) { writer.Write(1, 1); writer.Write(0, 1); }
            writer.Write(0, 2); writer.Write(0, 1); writer.Write(1, 1); writer.Write(1, 1); writer.Write(0, 5);
            for (int block = 0; block < blocks; block++) { writer.Write(block == 0 ? exponentStrategy : 0, 2); }
            if (blocks == 6) { writer.Write(0, 5); } else { writer.Write(0, 1); }
            writer.Write(63, 6); writer.Write(15, 4);
            if (blocks > 1) { writer.Write(0, 1); }
        }
    }
}
