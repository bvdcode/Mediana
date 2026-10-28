using System.Buffers.Binary;

namespace Mediana.Containers.MpegTs
{
    internal static class MpegTsTables
    {
        public const int ProgramMapPid = 0x1000;
        public const int FirstMediaPid = 0x100;

        public static void Pat(Span<byte> packet, int counter)
        {
            MpegTsPacket.Header(packet, 0, counter, true, 1);
            packet[4] = 0;
            Span<byte> section = packet.Slice(5, 16);
            new byte[] { 0, 0xB0, 13, 0, 1, 0xC1, 0, 0, 0, 1, 0xF0, 0 }.CopyTo(section);
            BinaryPrimitives.WriteUInt32BigEndian(section[12..], Crc(section[..12]));
        }

        public static void Pmt(Span<byte> packet, int counter, int pcrPid, ReadOnlySpan<byte> streamTypes)
        {
            MpegTsPacket.Header(packet, ProgramMapPid, counter, true, 1);
            packet[4] = 0;
            Span<byte> section = packet.Slice(5, 16 + streamTypes.Length * 5);
            section.Clear();
            section[0] = 2;
            BinaryPrimitives.WriteUInt16BigEndian(section[1..], (ushort)(0xB000 | section.Length - 3));
            section[4] = 1;
            section[5] = 0xC1;
            BinaryPrimitives.WriteUInt16BigEndian(section[8..], (ushort)(0xE000 | pcrPid));
            BinaryPrimitives.WriteUInt16BigEndian(section[10..], 0xF000);
            for (int index = 0; index < streamTypes.Length; index++)
            {
                int offset = 12 + index * 5;
                section[offset] = streamTypes[index];
                BinaryPrimitives.WriteUInt16BigEndian(section[(offset + 1)..], (ushort)(0xE000 | FirstMediaPid + index));
                BinaryPrimitives.WriteUInt16BigEndian(section[(offset + 3)..], 0xF000);
            }
            BinaryPrimitives.WriteUInt32BigEndian(section[^4..], Crc(section[..^4]));
        }

        public static uint Crc(ReadOnlySpan<byte> data)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in data)
            {
                crc ^= (uint)value << 24;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x80000000) != 0 ? crc << 1 ^ 0x04C11DB7 : crc << 1;
                }
            }
            return crc;
        }
    }
}
