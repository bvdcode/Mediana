// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    /// <summary>AV1 tile symbol arithmetic. Borrows encoded bytes and caller-owned CDF storage.
    /// Zero padding is virtual and bounded by the normative exit check; physical trailing bits are validated.</summary>
    internal ref struct Av1SymbolReader
    {
        private const int ProbabilityShift = 6;
        private const int MinimumProbability = 4;
        private readonly ReadOnlySpan<byte> encoded;
        private readonly bool updateCdf;
        private long position, remaining;
        private int range, value;
        private bool finished;

        internal Av1SymbolReader(ReadOnlySpan<byte> encoded, bool updateCdf = true)
        {
            if (encoded.IsEmpty) { throw new InvalidDataException("An AV1 tile entropy partition is empty."); }
            this.encoded = encoded; this.updateCdf = updateCdf;
            int count = (int)Math.Min((long)encoded.Length * 8, 15);
            int initial = ReadBits(count) << (15 - count);
            value = (Av1Cdf.Top - 1) ^ initial; range = Av1Cdf.Top;
            remaining = (long)encoded.Length * 8 - 15;
        }

        internal int Range => range;
        internal int Value => value;
        internal long BitsConsumed => (long)encoded.Length * 8 - remaining - 14;

        internal int ReadSymbol(scoped Span<ushort> cdf)
        {
            EnsureActive(); Av1Cdf.Validate(cdf);
            int result = Decode(cdf[..^1]);
            if (updateCdf) { Av1Cdf.Update(cdf, result); }
            return result;
        }

        internal bool ReadBoolean()
        {
            EnsureActive();
            ReadOnlySpan<ushort> cdf = [Av1Cdf.Top / 2, Av1Cdf.Top];
            return Decode(cdf) != 0;
        }

        internal uint ReadLiteral(int count)
        {
            EnsureActive(); ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 32);
            uint result = 0;
            for (int index = 0; index < count; index++) { result = (result << 1) | (ReadBoolean() ? 1U : 0U); }
            return result;
        }

        internal int ReadNs(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
            int width = BitOperations.Log2((uint)count) + 1;
            int cutoff = (int)((1L << width) - count);
            int value = (int)ReadLiteral(width - 1);
            return value < cutoff ? value : (value << 1) - cutoff + (ReadBoolean() ? 1 : 0);
        }

        private int Decode(scoped ReadOnlySpan<ushort> cdf)
        {
            int symbols = cdf.Length, current = range, previous, symbol = -1;
            do
            {
                symbol++; previous = current;
                int inverse = Av1Cdf.Top - cdf[symbol];
                current = ((range >> 8) * (inverse >> ProbabilityShift)) >> (7 - ProbabilityShift);
                current += MinimumProbability * (symbols - symbol - 1);
            } while (value < current);
            range = previous - current; value -= current;
            int shift = 15 - BitOperations.Log2((uint)range);
            range <<= shift;
            int count = (int)Math.Min(shift, Math.Max(0, remaining));
            int data = ReadBits(count) << (shift - count);
            value = data ^ (((value + 1) << shift) - 1);
            remaining -= shift;
            return symbol;
        }

        internal void Finish()
        {
            EnsureActive();
            if (remaining < -14) { throw new InvalidDataException("The AV1 symbol decoder consumed too many virtual padding bits."); }
            long trailing = position - Math.Min(15, remaining + 15);
            if (trailing < 0 || trailing >= (long)encoded.Length * 8)
            { throw new InvalidDataException("The AV1 entropy trailing bit is missing."); }
            int index = (int)(trailing >> 3), mask = 1 << (7 - (int)(trailing & 7));
            if ((encoded[index] & mask) == 0 || (encoded[index] & (mask - 1)) != 0 ||
                encoded[(index + 1)..].IndexOfAnyExcept((byte)0) >= 0)
            { throw new InvalidDataException("AV1 entropy trailing bits require one followed only by zeros."); }
            finished = true;
        }

        private int ReadBits(int count)
        {
            if (count == 0) { return 0; }
            int index = (int)(position >> 3), offset = (int)(position & 7);
            int cache = encoded[index] << 16;
            if (offset + count > 8) { cache |= encoded[index + 1] << 8; }
            if (offset + count > 16) { cache |= encoded[index + 2]; }
            position += count;
            return (cache >> (24 - offset - count)) & ((1 << count) - 1);
        }

        private void EnsureActive()
        {
            if (finished) { throw new InvalidOperationException("The AV1 entropy partition has already ended."); }
        }
    }
}
