// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal static class Mp4RecoveryGroupWriter
    {
        public static void WriteDescription(Mp4BoxWriter writer, Mp4RecoveryDescription description)
        {
            int box = writer.Full("sgpd", 0x01000000);
            foreach (char value in description.GroupingType) { writer.Byte((byte)value); }
            writer.UInt32(2);
            writer.UInt32(1);
            writer.UInt16(unchecked((ushort)description.Distance));
            writer.End(box);
        }

        public static void WriteMapping(Mp4BoxWriter writer, Mp4RecoveryDescription description, int sampleCount)
        {
            int box = writer.Full("sbgp");
            foreach (char value in description.GroupingType) { writer.Byte((byte)value); }
            writer.UInt32(1);
            writer.UInt32(checked((uint)sampleCount));
            writer.UInt32(1);
            writer.End(box);
        }
    }
}
