// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Pnm
{
    internal ref struct PnmTokens(ReadOnlySpan<byte> encoded, CancellationToken token)
    {
        private readonly ReadOnlySpan<byte> data = encoded;
        public int Position { get; private set; }
        public void Advance(int count)
        {
            if (count < 0 || count > data.Length - Position) { throw new InvalidDataException("Truncated PNM raster."); }
            Position += count;
        }
        public void SkipSpace()
        {
            while (Position < data.Length)
            {
                if ((Position & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                if (White(data[Position])) { Position++; }
                else if (data[Position] == '#') { SkipComment(); }
                else { break; }
            }
        }
        private void SkipComment()
        {
            while (Position < data.Length && data[Position] is not (10 or 13))
            {
                if ((Position & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                Position++;
            }
        }
        public int Number()
        {
            SkipSpace();
            int start = Position, value = 0;
            while (Position < data.Length && data[Position] is >= (byte)'0' and <= (byte)'9')
            {
                if ((Position & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                int digit = data[Position++] - '0';
                if (value > (int.MaxValue - digit) / 10) { throw new InvalidDataException("PNM number exceeds its supported range."); }
                value = value * 10 + digit;
            }
            if (Position == start || Position < data.Length && !White(data[Position]) && data[Position] != '#')
            {
                throw new InvalidDataException("Invalid PNM number.");
            }
            return value;
        }
        public int Bit()
        {
            SkipSpace();
            if (Position == data.Length || data[Position] is not ((byte)'0' or (byte)'1'))
            {
                throw new InvalidDataException("Invalid plain PBM pixel.");
            }
            return data[Position++] - '0';
        }
        public void RasterDelimiter()
        {
            if (Position < data.Length && data[Position] == '#') { SkipComment(); }
            if (Position == data.Length || !White(data[Position])) { throw new InvalidDataException("Missing PNM raster separator."); }
            Position++;
        }
        public static bool White(byte value) => value is 9 or 10 or 11 or 12 or 13 or 32;
    }
}
