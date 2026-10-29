// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Reads raw byte sequence payload bits with the most significant bit first.
    /// </summary>
    /// <remarks>
    /// Positions count bits after removal of emulation-prevention bytes. A 32-bit field
    /// is returned as the same signed bit pattern, without arithmetic overflow.
    /// </remarks>
    internal class BitReader(ArraySegment<byte> data)
    {
        private int position;
        public int BitsRemaining => checked((data.Count * 8) - position);
        public bool ByteAligned() => (position & 7) == 0;
        public ulong GetBitPosition() => (ulong)position;

        public int Read(int count)
        {
            if ((count < 0) || (count > 32))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (count > BitsRemaining)
            {
                throw new InvalidDataException("Truncated HEVC bitstream.");
            }
            uint result = 0;
            int remaining = count;
            while (remaining > 0)
            {
                int take = Math.Min(remaining, 8 - (position & 7));
                result = (result << take) | (uint)((data.Array![data.Offset + (position >> 3)] >> (8 - (position & 7) - take)) & ((1 << take) - 1));
                position += take;
                remaining -= take;
            }

            return unchecked((int)result);
        }

        public int ReadBit()
        {
            int byteIndex = position >> 3;
            if (byteIndex >= data.Count)
            {
                throw new InvalidDataException("Truncated HEVC bitstream.");
            }

            int value = (data.Array![data.Offset + byteIndex] >> (7 - (position & 7))) & 1;
            position++;
            return value;
        }

        public bool ReadFlag() => ReadBit() != 0;
        public int Peek(int count)
        {
            int saved = position;
            int value = Read(count);
            position = saved;
            return value;
        }

        public uint ReadUev()
        {
            int leading = 0;
            while (!ReadFlag())
            {
                if (++leading > 30)
                {
                    throw new InvalidDataException("Exp-Golomb value exceeds supported range.");
                }
            }

            return ((1u << leading) - 1u) + (uint)Read(leading);
        }

        public int ReadSev()
        {
            uint value = ReadUev();
            return (value & 1) == 0 ? -(int)(value >> 1) : (int)((value + 1) >> 1);
        }

        public void SkipBits(int count)
        {
            if ((count < 0) || (count > BitsRemaining))
            {
                throw new InvalidDataException("Bit skip exceeds RBSP.");
            }
            position += count;
        }

        public bool HasMoreRbspData(ulong endPosition)
        {
            int bits = checked((int)endPosition - position);
            if (bits <= 0)
            {
                return false;
            }
            if (bits > 8)
            {
                return true;
            }
            return Peek(bits) != (1 << (bits - 1));
        }
    }
}
