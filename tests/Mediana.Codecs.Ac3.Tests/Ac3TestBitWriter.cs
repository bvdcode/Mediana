// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3.Tests
{
    internal class Ac3TestBitWriter(int length)
    {
        private readonly byte[] data = new byte[length];
        private int position;
        public void Write(int value, int count)
        {
            for (int bit = count - 1; bit >= 0; bit--, position++) { data[position / 8] |= (byte)((value >> bit & 1) << (7 - position % 8)); }
        }
        public byte[] Finish(bool enhanced)
        {
            SetChecksums(data, enhanced); return data;
        }
        public static byte[] WithBit(byte[] source, int position, bool value, bool enhanced = true)
        {
            byte[] data = (byte[])source.Clone();
            int mask = 1 << (7 - position % 8);
            data[position / 8] = (byte)(value ? data[position / 8] | mask : data[position / 8] & ~mask);
            SetChecksums(data, enhanced); return data;
        }
        public static void SetChecksums(Span<byte> data, bool enhanced)
        {
            if (!enhanced)
            {
                data[2] = 0; data[3] = 0;
                int words = data.Length / 2, end = (words / 2 + words / 8) * 2;
                int target = Crc(data[2..end]);
                Span<int> remainderBasis = stackalloc int[16], wordBasis = stackalloc int[16];
                remainderBasis.Clear(); wordBasis.Clear();
                for (int bit = 0; bit < 16; bit++)
                {
                    int word = 1 << bit;
                    int remainder = Step(Step(0, word >> 8), word & 255);
                    for (int index = 4; index < end; index++) { remainder = Step(remainder, 0); }
                    for (int pivot = 15; pivot >= 0; pivot--)
                    {
                        if ((remainder & 1 << pivot) == 0) { continue; }
                        if (remainderBasis[pivot] == 0) { remainderBasis[pivot] = remainder; wordBasis[pivot] = word; break; }
                        remainder ^= remainderBasis[pivot]; word ^= wordBasis[pivot];
                    }
                }
                int result = 0;
                for (int pivot = 15; pivot >= 0; pivot--)
                {
                    if ((target & 1 << pivot) != 0) { target ^= remainderBasis[pivot]; result ^= wordBasis[pivot]; }
                }
                Assert.Equal(0, target);
                data[2] = (byte)(result >> 8); data[3] = (byte)result;
                Assert.Equal(0, Crc(data[2..end]));
            }
            int crc = Crc(data[2..^2]); data[^2] = (byte)(crc >> 8); data[^1] = (byte)crc;
            Assert.Equal(0, Crc(data[2..]));
        }
        private static int Crc(ReadOnlySpan<byte> data)
        {
            int value = 0;
            foreach (byte item in data) { value = Step(value, item); }
            return value;
        }
        private static int Step(int remainder, int value)
        {
            remainder ^= value << 8;
            for (int bit = 0; bit < 8; bit++) { remainder = (remainder << 1 ^ ((remainder & 0x8000) != 0 ? 0x8005 : 0)) & 0xffff; }
            return remainder;
        }
    }
}
