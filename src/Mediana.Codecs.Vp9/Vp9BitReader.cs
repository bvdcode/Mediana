// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal ref struct Vp9BitReader(ReadOnlySpan<byte> encoded)
    {
        private readonly ReadOnlySpan<byte> data = encoded;
        private int byteOffset, bitsRemaining;
        private int current;
        internal int ByteOffset => byteOffset;

        internal int Read(int bits)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bits);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(bits, 24);
            int result = 0;
            while (bits != 0)
            {
                if (bitsRemaining == 0)
                {
                    if (byteOffset == data.Length) { throw new InvalidDataException("Truncated VP9 uncompressed header."); }
                    current = data[byteOffset++]; bitsRemaining = 8;
                }
                int take = Math.Min(bits, bitsRemaining); bitsRemaining -= take;
                result = (result << take) | ((current >> bitsRemaining) & ((1 << take) - 1)); bits -= take;
            }
            return result;
        }

        internal bool Flag() => Read(1) != 0;
        internal int Signed(int bits) { int value = Read(bits); return Flag() ? -value : value; }
        internal void Align()
        {
            if (Read(bitsRemaining) != 0) { throw new InvalidDataException("VP9 alignment bits must be zero."); }
        }
    }
}
