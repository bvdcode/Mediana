// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Mediana.Formats.Video
{
    /// <summary>Reads an H.264 EBSP payload with checked emulation prevention, alignment and RBSP endings.</summary>
    public ref struct H264BitReader(ReadOnlySpan<byte> payload)
    {
        private readonly ReadOnlySpan<byte> data = payload;
        private int offset;
        private int available;
        private ulong buffer;
        private int zeros;
        private int preventionBytes;

        internal H264BitReader(ReadOnlySpan<byte> payload, scoped in H264BitReaderCheckpoint checkpoint)
            : this(payload)
        {
            offset = checkpoint.Offset;
            available = checkpoint.BufferedBits;
            buffer = checkpoint.Buffer;
            zeros = checkpoint.ZeroBytes;
            preventionBytes = checkpoint.PreventionBytes;
        }

        internal H264BitReaderCheckpoint CreateCheckpoint() => new(offset, available, buffer, zeros, preventionBytes);

        public bool ByteAligned => (available & 7) == 0;
        public int BitPosition => checked((int)(((long)offset - preventionBytes) * 8 - available));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadBits(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 32);
            if (count > available)
            {
                Fill(count);
            }
            available -= count;
            return (uint)((buffer >> available) & ((1UL << count) - 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBit()
        {
            if (available == 0)
            {
                Fill(1);
            }
            return (int)(buffer >> --available) & 1;
        }

        internal uint ReadCabacLookahead(int required, int maximum, out int count)
        {
            if (required > available)
            {
                Fill(required);
            }
            count = Math.Min(maximum, available);
            available -= count;
            return (uint)(buffer >> available) & ((1u << count) - 1);
        }

        internal void ReturnCabacLookahead(int count) => available += count;

        private void Fill(int count)
        {
            do
            {
                if (data.Length - offset >= sizeof(uint) && zeros < 2)
                {
                    uint word = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                    // A run of nonzero bytes cannot contain or start an emulation-prevention sequence.
                    if ((unchecked(word - 0x01010101u) & ~word & 0x80808080u) == 0)
                    {
                        buffer = (buffer << 32) | word;
                        available += 32;
                        offset += sizeof(uint);
                        zeros = 0;
                        return;
                    }
                }
                buffer = (buffer << 8) | (uint)ReadByte();
                available += 8;
            }
            while (available < count);
        }

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
            if (available != 0 || offset != data.Length)
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
            available -= available & 7;
            while (available != 0 || offset < data.Length)
            {
                if (available == 0 && zeros == 2 && offset == data.Length - 1 && data[offset] == 3)
                {
                    offset++;
                    preventionBytes++;
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
                available = 0;
                throw new InvalidDataException("The H.264 bitstream is truncated.");
            }
            int value = data[offset++];
            if (zeros == 2)
            {
                if (value == 3)
                {
                    if (offset == data.Length || data[offset] > 3)
                    {
                        available = 0;
                        throw new InvalidDataException("The H.264 emulation prevention byte is invalid.");
                    }
                    value = data[offset++];
                    preventionBytes++;
                    zeros = 0;
                }
                else if (value < 3)
                {
                    available = 0;
                    throw new InvalidDataException("An H.264 emulation prevention byte is missing.");
                }
            }
            zeros = value == 0 ? zeros + 1 : 0;
            return value;
        }
    }
}
