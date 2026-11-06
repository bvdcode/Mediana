// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal ref struct VorbisBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> bytes = data;
        private long position;
        public readonly long Remaining => (long)bytes.Length * 8 - position;

        public uint Read(int count)
        {
            if (count is < 0 or > 32) { throw new ArgumentOutOfRangeException(nameof(count)); }
            if (count > Remaining) { throw new InvalidDataException("The Vorbis bitstream is truncated."); }
            uint value = 0;
            int written = 0;
            while (written < count)
            {
                int offset = (int)(position & 7);
                int length = Math.Min(8 - offset, count - written);
                uint bits = (uint)(bytes[(int)(position >> 3)] >> offset) & ((1u << length) - 1);
                value |= bits << written;
                position += length;
                written += length;
            }
            return value;
        }

        public void Skip(long count)
        {
            if (count < 0 || count > Remaining) { throw new InvalidDataException("The Vorbis bitstream is truncated."); }
            position += count;
        }

        public static int ILog(uint value) => 32 - System.Numerics.BitOperations.LeadingZeroCount(value);
    }
}
