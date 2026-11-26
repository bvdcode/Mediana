// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.Ffv1
{
    internal ref struct Ffv1RangeReader
    {
        private static readonly byte[] Increasing = BuildTransitions();
        private readonly ReadOnlySpan<byte> bytes;
        private ReadOnlySpan<byte> transitions;
        private int range = 0xFF00;
        private int low;
        private int offset = 2;
        private int virtualBytes;

        internal Ffv1RangeReader(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2)
            {
                throw new InvalidDataException("The FFV1 range-coded header is truncated.");
            }
            bytes = data;
            low = BinaryPrimitives.ReadUInt16BigEndian(data);
            if (low >= range)
            {
                throw new InvalidDataException("The FFV1 range-coded header starts outside its coding interval.");
            }
            transitions = Increasing;
        }

        internal readonly int BytesRead => offset;
        internal readonly int RiceOffset => offset - 1;

        internal static int DefaultTransition(int index) => Increasing[index];

        internal void SetTransitions(ReadOnlySpan<byte> table) => transitions = table;

        internal bool ReadBit(scoped ref byte state)
        {
            int upper = range * state >> 8;
            range -= upper;
            bool bit = low >= range;
            if (bit)
            {
                low -= range;
                range = upper;
                state = transitions[state];
            }
            else
            {
                state = unchecked((byte)(256 - transitions[256 - state]));
            }
            if (range < 256)
            {
                range <<= 8;
                low <<= 8;
                if (offset < bytes.Length)
                {
                    low |= bytes[offset++];
                }
                else if (++virtualBytes > 2)
                {
                    throw new InvalidDataException("The FFV1 range-coded payload is truncated.");
                }
            }
            return bit;
        }

        internal int ReadSymbol(scoped Span<byte> states, bool signed = false)
        {
            if (ReadBit(ref states[0]))
            {
                return 0;
            }
            int exponent = 0;
            while (ReadBit(ref states[1 + Math.Min(9, exponent)]))
            {
                if (++exponent == 31)
                {
                    throw new InvalidDataException("The FFV1 range-coded integer exceeds its supported width.");
                }
            }
            int magnitude = 1;
            for (int power = exponent - 1; power >= 0; power--)
            {
                magnitude = 2 * magnitude + (ReadBit(ref states[22 + Math.Min(9, power)]) ? 1 : 0);
            }
            return signed && ReadBit(ref states[11 + Math.Min(10, exponent)]) ? -magnitude : magnitude;
        }

        private static byte[] BuildTransitions()
        {
            const long scale = 1L << 32;
            const long adaptation = scale / 20;
            const int maximum = 248;
            const int minimum = 8;
            byte[] result = new byte[256];
            long probability = scale / 2;
            int previous = 0;
            for (int i = 0; i < 128; i++)
            {
                int next = Math.Max(previous + 1, (int)((probability * 256 + scale / 2) >> 32));
                if (previous is > 0 and < 256 && next <= maximum)
                {
                    result[previous] = (byte)next;
                }
                probability += ((scale - probability) * adaptation + scale / 2) >> 32;
                previous = next;
            }
            for (int i = minimum; i <= maximum; i++)
            {
                if (result[i] == 0)
                {
                    probability = ((i * scale + 128) >> 8);
                    probability += ((scale - probability) * adaptation + scale / 2) >> 32;
                    int next = (int)((probability * 256 + scale / 2) >> 32);
                    result[i] = (byte)Math.Clamp(next, Math.Min(i + 1, maximum), maximum);
                }
            }
            return result;
        }
    }
}
