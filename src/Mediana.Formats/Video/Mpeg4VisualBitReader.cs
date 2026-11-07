// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Formats.Video
{
    /// <summary>Checked MSB-first reader for MPEG-4 Visual byte streams.</summary>
    public ref struct Mpeg4VisualBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private long position;
        public readonly long BitPosition => position;
        public readonly long RemainingBits => (long)data.Length * 8 - position;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(int count)
        {
            int result = Peek(count); position += count; return result;
        }
        public bool Flag() => Read(1) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int Peek(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 24);
            if (count > RemainingBits) { throw new InvalidDataException("The MPEG-4 Visual bitstream is truncated."); }
            if (count == 0) { return 0; }
            int index = (int)(position >> 3), shift = (int)(position & 7);
            uint value = (uint)data[index] << 24;
            if (shift + count > 8) { value |= (uint)data[index + 1] << 16; }
            if (shift + count > 16) { value |= (uint)data[index + 2] << 8; }
            if (shift + count > 24) { value |= data[index + 3]; }
            return (int)((value << shift) >> (32 - count));
        }
        public void Skip(int count) { _ = Read(count); }
        public void Marker()
        {
            if (!Flag()) { throw new InvalidDataException("An MPEG-4 Visual marker bit is missing."); }
        }
    }
}
