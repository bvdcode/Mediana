// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3.Tests
{
    internal static class Ac3AdaptiveFrame
    {
        public static byte[] Create(bool switched)
        {
            Ac3TestBitWriter writer = new(2048);
            writer.Write(0xb77, 16); writer.Write(0, 2); writer.Write(0, 3); writer.Write(1023, 11);
            writer.Write(0, 2); writer.Write(3, 2); writer.Write(1, 3); writer.Write(0, 1); writer.Write(16, 5);
            writer.Write(31, 5); writer.Write(0, 4);
            writer.Write(1, 1); writer.Write(1, 1); writer.Write(0, 2); writer.Write(0, 1);
            writer.Write(1, 1); writer.Write(1, 1); writer.Write(0, 5);
            for (int block = 0; block < 6; block++) { writer.Write(block == 0 ? 1 : 0, 2); }
            writer.Write(0, 5); writer.Write(1, 1); writer.Write(63, 6); writer.Write(15, 4); writer.Write(0, 1);
            for (int block = 0; block < 6; block++)
            {
                writer.Write(switched && block % 2 == 1 ? 1 : 0, 1); writer.Write(0, 3);
                if (block == 0)
                {
                    writer.Write(0, 6); writer.Write(0, 4);
                    for (int group = 0; group < 24; group++) { writer.Write(62, 7); }
                    writer.Write(0, 2);
                }
                writer.Write(0, 1);
                if (block != 0) { continue; }
                writer.Write(0, 2);
                for (int bin = 0; bin < 73; bin++)
                {
                    for (int item = 0; item < 6; item++)
                    { writer.Write((bin, item) switch { (3, 0) => 330, (3, 1) => -170, (19, 3) => 51, _ => 0 }, 16); }
                }
            }
            return writer.Finish(true);
        }
    }
}
