// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Alac
{
    internal ref struct AlacBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        public int Position { get; private set; }
        public long Remaining => (long)data.Length * 8 - Position;

        public uint Read(int count)
        {
            if (count is < 0 or > 32 || Remaining < count)
            {
                throw new InvalidDataException("The ALAC bitstream is truncated.");
            }
            uint result = 0;
            while (count != 0)
            {
                int available = 8 - (Position & 7);
                int take = Math.Min(available, count);
                result = (result << take) | (uint)((data[Position >> 3] >> (available - take)) & ((1 << take) - 1));
                Position += take;
                count -= take;
            }
            return result;
        }

        public int Signed(int count)
        {
            uint value = Read(count);
            return unchecked((int)(value << (32 - count))) >> (32 - count);
        }

        public void Skip(int count)
        {
            if (count < 0 || Remaining < count)
            {
                throw new InvalidDataException("The ALAC bitstream is truncated.");
            }
            Position = checked(Position + count);
        }

        public uint Peek(int count)
        {
            AlacBitReader copy = this;
            return copy.Read(count);
        }

        public void Align() => Skip((8 - (Position & 7)) & 7);
    }
}
