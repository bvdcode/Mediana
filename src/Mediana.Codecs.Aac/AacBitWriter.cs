// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal ref struct AacBitWriter(Span<byte> destination)
    {
        private readonly Span<byte> destination = destination;
        public int Position { get; private set; }

        public void Write(uint value, int count)
        {
            if (count is < 0 or > 32 || (count < 32 && value >> count != 0))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (Position > destination.Length * 8 - count)
            {
                throw new NotSupportedException("The AAC access unit exceeds its bit budget.");
            }
            for (int bit = count - 1; bit >= 0; bit--)
            {
                int shift = 7 - (Position & 7);
                if (shift == 7)
                {
                    destination[Position >> 3] = 0;
                }
                destination[Position >> 3] |= (byte)((value >> bit & 1) << shift);
                Position++;
            }
        }

        public int Finish()
        {
            Write(7, 3);
            Write(0, (8 - (Position & 7)) & 7);
            return Position / 8;
        }
    }
}
