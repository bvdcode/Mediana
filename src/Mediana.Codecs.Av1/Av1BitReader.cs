// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref struct Av1BitReader(ReadOnlySpan<byte> encoded)
    {
        private readonly ReadOnlySpan<byte> data = encoded;
        private int position;
        internal int ByteOffset => (position + 7) >> 3;
        internal int RemainingBits => checked(data.Length * 8 - position);
        internal bool Flag() => Read(1) != 0;

        internal int Read(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 24);
            if (count > RemainingBits)
            {
                throw new InvalidDataException("The AV1 uncompressed header is truncated.");
            }
            int value = 0;
            for (int index = 0; index < count; index++)
            {
                value = (value << 1) | ((data[position >> 3] >> (7 - (position & 7))) & 1);
                position++;
            }
            return value;
        }

        internal int ReadSigned(int count)
        {
            int value = Read(count);
            return (value << (32 - count)) >> (32 - count);
        }

        internal uint ReadUnsigned(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 32);
            if (count <= 24) { return (uint)Read(count); }
            return ((uint)Read(count - 16) << 16) | (uint)Read(16);
        }

        internal uint ReadUvlc()
        {
            int leadingZeros = 0;
            while (!Flag())
            {
                if (leadingZeros < 32) { leadingZeros++; }
            }
            if (leadingZeros == 32) { return uint.MaxValue; }
            return ReadUnsigned(leadingZeros) + (1U << leadingZeros) - 1;
        }

        internal int ReadNs(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
            int width = BitOperations.Log2((uint)count) + 1;
            int cutoff = (int)((1L << width) - count);
            int value = Read(width - 1);
            return value < cutoff ? value : (value << 1) - cutoff + Read(1);
        }

        internal void Align()
        {
            while ((position & 7) != 0)
            {
                if (Flag())
                {
                    throw new InvalidDataException("AV1 byte alignment requires zero bits.");
                }
            }
        }

        internal void Finish()
        {
            if (!Flag())
            {
                throw new InvalidDataException("The AV1 OBU trailing one bit is missing.");
            }
            while (RemainingBits > 0)
            {
                if (Flag())
                {
                    throw new InvalidDataException("The AV1 OBU trailing padding is not zero.");
                }
            }
        }
    }
}
