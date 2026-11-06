// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Opus
{
    /// <summary>RFC 6716 range and reverse raw-bit decoding for one borrowed compressed frame.
    /// Range normalization uses the required zero extension; raw bits may overlap its lookahead.</summary>
    internal ref struct OpusRangeDecoder
    {
        private readonly ReadOnlySpan<byte> frame;
        private uint range;
        private uint value;
        private int front;
        private int back;
        private int previousByte;
        private int totalBits;
        private int rawBits;
        private int bufferedRawBits;
        private ulong rawBuffer;

        public OpusRangeDecoder(ReadOnlySpan<byte> frame)
        {
            this.frame = frame;
            range = 128;
            totalBits = 9;
            previousByte = ReadFront();
            value = 127U - (uint)(previousByte >> 1);
            Normalize();
        }

        public readonly uint Range => range;
        public readonly int BitsUsed => totalBits - ILog(range);

        public readonly int EighthBitsUsed
        {
            get
            {
                int logarithm = ILog(range);
                uint fraction = range >> (logarithm - 16);
                for (int bit = 0; bit < 3; bit++)
                {
                    fraction = fraction * fraction >> 15;
                    int extra = (int)(fraction >> 16);
                    logarithm = (logarithm << 1) | extra;
                    fraction >>= extra;
                }
                return checked(totalBits * 8 - logarithm);
            }
        }

        public bool ReadBit(int probabilityLog2)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(probabilityLog2);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(probabilityLog2, 31);
            uint split = range >> probabilityLog2;
            bool result = value < split;
            if (result) { range = split; }
            else
            {
                value -= split;
                range -= split;
            }
            Normalize();
            return result;
        }

        /// <summary>The inverse cumulative table ends in zero and uses a power-of-two total up to 256.</summary>
        public int ReadInverseCdf(ReadOnlySpan<byte> table, int totalBits = 8)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalBits);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(totalBits, 8);
            if (table.IsEmpty || table[^1] != 0 || table[0] >= 1 << totalBits)
            {
                throw new ArgumentException("An Opus inverse cumulative table requires a valid first count and a zero terminator.", nameof(table));
            }
            uint unit = range >> totalBits;
            uint upper = range;
            for (int symbol = 0; symbol < table.Length; symbol++)
            {
                uint lower = unit * table[symbol];
                if (lower > upper) { throw new ArgumentException("Opus inverse cumulative counts must not increase.", nameof(table)); }
                if (value >= lower)
                {
                    value -= lower;
                    range = upper - lower;
                    Normalize();
                    return symbol;
                }
                upper = lower;
            }
            throw new InvalidDataException("The range value lies outside its inverse cumulative model.");
        }

        public int ReadFrequencies(ReadOnlySpan<ushort> frequencies)
        {
            uint total = 0;
            foreach (ushort frequency in frequencies) { total = checked(total + frequency); }
            if (total is 0 or > ushort.MaxValue) { throw new ArgumentException("Opus frequencies require a nonzero 16-bit total.", nameof(frequencies)); }
            uint unit = range / total;
            uint target = total - Math.Min(value / unit + 1, total);
            uint low = 0;
            for (int symbol = 0; symbol < frequencies.Length; symbol++)
            {
                uint high = low + frequencies[symbol];
                if (target < high)
                {
                    Apply(low, high, total, unit);
                    return symbol;
                }
                low = high;
            }
            throw new InvalidDataException("The range value lies outside its frequency model.");
        }

        public uint ReadUniform(uint count)
        {
            ArgumentOutOfRangeException.ThrowIfZero(count);
            int raw = Math.Max(0, ILog(count - 1) - 8);
            uint total = ((count - 1) >> raw) + 1;
            uint unit = range / total;
            uint upper = total - Math.Min(value / unit + 1, total);
            Apply(upper, upper + 1, total, unit);
            uint result = (upper << raw) | ReadRawBits(raw);
            if (result >= count) { throw new InvalidDataException("An Opus uniform integer exceeds its declared range."); }
            return result;
        }

        /// <summary>Decodes CELT's symmetric signed energy residual with Q15 probabilities.</summary>
        public int ReadLaplace(int zeroProbability, int decay)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zeroProbability);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(zeroProbability, 32736);
            ArgumentOutOfRangeException.ThrowIfNegative(decay);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(decay, 16384);
            const uint total = 32768;
            uint unit = range >> 15;
            uint rank = total - Math.Min(value / unit + 1, total);
            uint start = (uint)zeroProbability;
            if (rank < start)
            {
                Apply(0, start, total, unit);
                return 0;
            }
            uint residual = rank - start;
            uint weight = 1 + ((32736u - start) * (uint)(16384 - decay) >> 15);
            int magnitude = 1;
            while (weight > 1 && residual >= 2 * weight)
            {
                residual -= 2 * weight;
                start += 2 * weight;
                weight = 1 + ((weight - 1) * (uint)(2 * decay) >> 15);
                magnitude++;
            }
            if (weight == 1)
            {
                magnitude += (int)(residual / 2);
                start += residual & ~1u;
                residual &= 1;
            }
            bool positive = residual >= weight;
            if (positive) { start += weight; }
            Apply(start, Math.Min(start + weight, total), total, unit);
            if (positive) { return magnitude; }
            return -magnitude;
        }

        public uint ReadRawBits(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 32);
            if (count > (long)frame.Length * 8 - rawBits) { throw new InvalidDataException("Opus raw bits exceed their frame."); }
            while (bufferedRawBits < count)
            {
                rawBuffer |= (ulong)frame[frame.Length - 1 - back++] << bufferedRawBits;
                bufferedRawBits += 8;
            }
            uint result = (uint)(rawBuffer & ((1UL << count) - 1));
            rawBuffer >>= count;
            bufferedRawBits -= count;
            rawBits += count;
            totalBits = checked(totalBits + count);
            return result;
        }

        private void Apply(uint low, uint high, uint total, uint unit)
        {
            uint tail = unit * (total - high);
            value -= tail;
            range = low == 0 ? range - tail : unit * (high - low);
            Normalize();
        }

        private void Normalize()
        {
            while (range <= 1 << 23)
            {
                range <<= 8;
                int next = ReadFront();
                int symbol = ((previousByte & 1) << 7) | (next >> 1);
                value = ((value << 8) + (uint)(255 - symbol)) & 0x7fffffff;
                previousByte = next;
                totalBits = checked(totalBits + 8);
            }
        }

        private int ReadFront() => front < frame.Length ? frame[front++] : 0;
        private static int ILog(uint value) => value == 0 ? 0 : 32 - BitOperations.LeadingZeroCount(value);
    }
}
