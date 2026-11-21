// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Audio
{
    /// <summary>Reads DTS Core bits directly from 16-bit or 14-bit words in either byte order.</summary>
    public ref struct DtsBitReader
    {
        private readonly ReadOnlySpan<byte> data;
        private readonly bool littleEndian;
        private readonly int wordBits;
        private int position;

        public DtsBitReader(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4) { throw new InvalidDataException("The DTS synchronization word is truncated."); }
            (littleEndian, wordBits) = BinaryPrimitives.ReadUInt32BigEndian(data) switch
            {
                0x7ffe8001 => (false, 16),
                0xfe7f0180 => (true, 16),
                0x1fffe800 => (false, 14),
                0xff1f00e8 => (true, 14),
                _ => throw new InvalidDataException("The DTS Core synchronization word is invalid.")
            };
            this.data = data;
            position = 0;
        }

        public int Position => position;
        public int WordBits => wordBits;
        public int Remaining => checked(data.Length / 2 * wordBits + (wordBits == 16 && !littleEndian ? data.Length % 2 * 8 : 0) - position);

        public int Read(int count)
        {
            if (count is < 0 or > 32 || count > Remaining) { throw new InvalidDataException("The DTS bitstream is truncated."); }
            uint value = 0;
            while (count != 0)
            {
                int word = position / wordBits;
                int available = wordBits - position % wordBits;
                int take = Math.Min(count, available);
                int offset = word * 2;
                int packed = littleEndian ? data[offset] | data[offset + 1] << 8 :
                    data[offset] << 8 | (offset + 1 < data.Length ? data[offset + 1] : 0);
                value = value << take | (uint)((packed >> (available - take)) & ((1 << take) - 1));
                position += take;
                count -= take;
            }
            return (int)value;
        }

        public int Signed(int count)
        {
            int value = Read(count);
            return value << (32 - count) >> (32 - count);
        }

        public void Skip(int count)
        {
            if (count < 0 || count > Remaining) { throw new InvalidDataException("The DTS bitstream is truncated."); }
            position += count;
        }
    }
}
