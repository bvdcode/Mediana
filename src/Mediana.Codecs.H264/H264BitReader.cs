// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal ref struct H264BitReader(ReadOnlySpan<byte> payload)
    {
        private readonly ReadOnlySpan<byte> data = payload;
        private int offset;
        private int available;
        private int current;
        private int zeros;

        public bool ByteAligned => available == 0;

        public uint ReadBits(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 32);
            uint value = 0;
            while (count > 0)
            {
                if (available == 0)
                {
                    current = ReadByte();
                    available = 8;
                }
                int take = Math.Min(available, count);
                value = (value << take) | (uint)((current >> (available - take)) & ((1 << take) - 1));
                available -= take;
                count -= take;
            }
            return value;
        }

        public int ReadBit() => (int)ReadBits(1);
        public bool ReadFlag() => ReadBit() != 0;

        public int ReadUe(int maximum = int.MaxValue)
        {
            int leading = 0;
            while (ReadBit() == 0)
            {
                if (++leading > 31)
                {
                    throw new InvalidDataException("An H.264 Exp-Golomb value exceeds the supported range.");
                }
            }
            uint value = ((1u << leading) - 1) + ReadBits(leading);
            if (value > maximum)
            {
                throw new InvalidDataException("An H.264 syntax value exceeds its permitted range.");
            }
            return (int)value;
        }

        public int ReadSe(int minimum = int.MinValue + 1, int maximum = int.MaxValue)
        {
            int code = ReadUe();
            int value = (code & 1) == 0 ? -(code / 2) : code / 2 + 1;
            if (value < minimum || value > maximum)
            {
                throw new InvalidDataException("An H.264 signed syntax value exceeds its permitted range.");
            }
            return value;
        }

        public bool MoreRbspData()
        {
            H264BitReader remaining = this;
            if (remaining.available == 0 && remaining.offset == remaining.data.Length)
            {
                return false;
            }
            if (remaining.ReadBit() == 0)
            {
                return true;
            }
            while (remaining.available != 0 || remaining.offset < remaining.data.Length)
            {
                if (remaining.ReadBit() != 0)
                {
                    return true;
                }
            }
            return false;
        }

        public void ReadTrailingBits()
        {
            if (ReadBit() != 1)
            {
                throw new InvalidDataException("The H.264 RBSP stop bit is missing.");
            }
            AlignZero();
            if (offset != data.Length)
            {
                throw new InvalidDataException("Unexpected bytes follow the H.264 RBSP trailing bits.");
            }
        }

        public void AlignOne()
        {
            while (!ByteAligned)
            {
                if (ReadBit() != 1)
                {
                    throw new InvalidDataException("An H.264 CABAC alignment bit is zero.");
                }
            }
        }

        /// <summary>Discards unused bits in the arithmetic terminal byte and validates following zero words.</summary>
        public void ReadCabacPadding()
        {
            available = 0;
            while (offset < data.Length)
            {
                if (zeros == 2 && data[offset] == 3 && offset == data.Length - 1)
                {
                    offset++;
                    break;
                }
                if (ReadBits(16) != 0)
                {
                    throw new InvalidDataException("The H.264 CABAC zero word is nonzero.");
                }
            }
        }

        public void AlignZero()
        {
            while (!ByteAligned)
            {
                if (ReadBit() != 0)
                {
                    throw new InvalidDataException("An H.264 alignment bit is nonzero.");
                }
            }
        }

        private int ReadByte()
        {
            if (offset == data.Length)
            {
                throw new InvalidDataException("The H.264 bitstream is truncated.");
            }
            int value = data[offset++];
            if (zeros == 2)
            {
                if (value == 3)
                {
                    if (offset == data.Length || data[offset] > 3)
                    {
                        throw new InvalidDataException("The H.264 emulation prevention byte is invalid.");
                    }
                    value = data[offset++];
                    zeros = 0;
                }
                else if (value < 3)
                {
                    throw new InvalidDataException("An H.264 emulation prevention byte is missing.");
                }
            }
            zeros = value == 0 ? zeros + 1 : 0;
            return value;
        }
    }
}
