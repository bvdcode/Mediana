// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal ref struct AacBitReader
    {
        private readonly ReadOnlySpan<byte> data;
        private int position;

        public AacBitReader(ReadOnlySpan<byte> data)
        {
            if (data.Length > int.MaxValue / 8)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }
            this.data = data;
        }

        public readonly int Remaining => data.Length * 8 - position;
        public readonly int Position => position;

        public readonly int PeekByte()
        {
            if (Remaining == 0)
            {
                throw new InvalidDataException("The AAC access unit is truncated.");
            }
            int index = position >> 3;
            int value = data[index] << 8;
            if (index + 1 < data.Length)
            {
                value |= data[index + 1];
            }
            return (value >> (8 - (position & 7))) & 255;
        }

        public int Read(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 24);
            if (count > Remaining)
            {
                throw new InvalidDataException("The AAC access unit is truncated.");
            }
            int result = 0;
            while (count > 0)
            {
                int take = Math.Min(count, 8 - (position & 7));
                int shift = 8 - (position & 7) - take;
                result = result << take | (data[position >> 3] >> shift & ((1 << take) - 1));
                count -= take;
                position += take;
            }
            return result;
        }

        public void Skip(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > Remaining)
            {
                throw new InvalidDataException("The AAC access unit is truncated.");
            }
            position += count;
        }

        public void Align() => Skip((8 - (position & 7)) & 7);
    }
}
