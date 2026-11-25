// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dv
{
    internal ref struct DvBitReservoir(Span<byte> data)
    {
        private readonly Span<byte> bytes = data;
        private int length;

        internal readonly DvBitReader Open() => new(bytes, length);

        internal void Append(scoped ref DvBitReader source)
        {
            while (source.Remaining != 0)
            {
                int offset = length & 7;
                int bits = Math.Min(source.Remaining, 8 - offset);
                int shifted = source.Read(bits) << (8 - offset - bits);
                int index = length >> 3;
                bytes[index] = offset == 0 ? (byte)shifted : (byte)(bytes[index] | shifted);
                length += bits;
            }
        }
    }
}
