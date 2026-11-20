// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal ref struct Mp3BitWriter(Span<byte> destination)
    {
        private readonly Span<byte> destination = destination;
        public int Position { get; private set; }

        public void Write(uint value, int count)
        {
            if ((uint)count > 32 || (count < 32 && value >> count != 0))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (Position > destination.Length * 8 - count)
            {
                throw new InvalidOperationException("The MP3 bit budget was exceeded.");
            }
            while (count > 0)
            {
                int take = Math.Min(count, 8 - (Position & 7));
                count -= take;
                int shift = 8 - (Position & 7) - take;
                byte mask = (byte)(((1u << take) - 1) << shift);
                destination[Position >> 3] = (byte)(((uint)destination[Position >> 3] & ~(uint)mask) | ((value >> count << shift) & mask));
                Position += take;
            }
        }
    }
}
