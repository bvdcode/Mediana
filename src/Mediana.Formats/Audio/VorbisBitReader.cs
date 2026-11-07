// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal ref struct VorbisBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> bytes = data;
        private long position;
        public readonly long Remaining => (long)bytes.Length * 8 - position;

        public readonly bool TryPeek(int count, out uint value)
        {
            if (count is < 0 or > 32) { throw new ArgumentOutOfRangeException(nameof(count)); }
            value = 0;
            if (count > Remaining) { return false; }
            if (count == 0) { return true; }
            int start = (int)(position >> 3), shift = (int)(position & 7);
            ulong packed;
            if (bytes.Length - start >= sizeof(ulong))
            {
                packed = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes[start..]);
            }
            else
            {
                packed = 0;
                int needed = (shift + count + 7) / 8;
                for (int index = 0; index < needed; index++) { packed |= (ulong)bytes[start + index] << (8 * index); }
            }
            value = (uint)(packed >> shift) & (uint.MaxValue >> (32 - count));
            return true;
        }

        public uint Read(int count)
        {
            if (!TryPeek(count, out uint value)) { throw new InvalidDataException("The Vorbis bitstream is truncated."); }
            position += count;
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
