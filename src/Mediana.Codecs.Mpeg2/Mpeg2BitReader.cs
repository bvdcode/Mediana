// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Mpeg2
{
    internal ref struct Mpeg2BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private long position;

        public readonly long Remaining => (long)data.Length * 8 - position;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(int width)
        {
            int value = Peek(width);
            position += width;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int Peek(int width)
        {
            if (width > Remaining)
            {
                throw new InvalidDataException("The MPEG-2 video bitstream is truncated.");
            }
            if (width == 0) { return 0; }
            int index = (int)(position >> 3);
            int shift = (int)(position & 7);
            uint window = (uint)data[index] << 24;
            if (shift + width > 8) { window |= (uint)data[index + 1] << 16; }
            if (shift + width > 16) { window |= (uint)data[index + 2] << 8; }
            if (shift + width > 24) { window |= data[index + 3]; }
            return (int)((window << shift) >> (32 - width));
        }

        public bool Flag() => Read(1) != 0;

        public void Skip(int width)
        {
            if (width > Remaining) { throw new InvalidDataException("The MPEG-2 video bitstream is truncated."); }
            position += width;
        }

        public void ReadMarker()
        {
            if (!Flag()) { throw new InvalidDataException("The MPEG-2 video marker bit is missing."); }
        }

        public readonly bool AtSliceEnd()
        {
            int prefixBits = (int)Math.Min(23, Remaining);
            if (prefixBits != 0 && Peek(prefixBits) != 0) { return false; }
            int nextByte = (int)((position + prefixBits + 7) >> 3);
            int alignedBits = (int)(nextByte * 8L - position);
            if ((alignedBits > prefixBits && Peek(alignedBits) != 0) ||
                data[nextByte..].IndexOfAnyExcept((byte)0) >= 0)
            {
                throw new InvalidDataException("Nonzero MPEG-2 data follows slice padding.");
            }
            return true;
        }
    }
}
