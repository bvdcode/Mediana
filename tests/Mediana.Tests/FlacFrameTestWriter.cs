// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class FlacFrameTestWriter
    {
        private readonly List<byte> bytes = [];
        private int position;

        public FlacFrameTestWriter(int samples = 16, int assignment = 0, long number = 0, bool variable = false,
            int precisionCode = 0, int rateCode = 0)
        {
            Write(0xfff8 | (variable ? 1 : 0), 16);
            Write(7, 4);
            Write(rateCode, 4);
            Write(assignment, 4);
            Write(precisionCode, 3);
            Write(0, 1);
            WriteNumber(number);
            Write(samples - 1, 16);
            if (rateCode is >= 12 and <= 14)
            {
                Write(rateCode switch { 12 => 48, 13 => 48000, 14 => 4800, _ => throw new InvalidOperationException() },
                    rateCode switch { 12 => 8, 13 or 14 => 16, _ => throw new InvalidOperationException() });
            }
            Write(Crc(bytes, 8, 0x07), 8);
        }

        public void Write(long value, int bits)
        {
            for (int bit = bits - 1; bit >= 0; bit--)
            {
                if (position % 8 == 0)
                {
                    bytes.Add(0);
                }
                bytes[^1] |= (byte)(((value >> bit) & 1) << (7 - position % 8));
                position++;
            }
        }

        public void Unary(uint zeros)
        {
            for (uint i = 0; i < zeros; i++)
            {
                Write(0, 1);
            }
            Write(1, 1);
        }

        public void Rice(long residual, int parameter)
        {
            ulong folded = (ulong)((residual << 1) ^ (residual >> 63));
            Unary((uint)(folded >> parameter));
            Write((long)folded, parameter);
        }

        public byte[] Finish()
        {
            Write(0, (8 - position % 8) % 8);
            Write(Crc(bytes, 16, 0x8005), 16);
            return bytes.ToArray();
        }

        public byte[] Prefix() => bytes.ToArray();

        private void WriteNumber(long value)
        {
            if (value < 128)
            {
                Write(value, 8);
                return;
            }
            int length = value switch
            {
                < 0x800 => 2,
                < 0x10000 => 3,
                < 0x200000 => 4,
                < 0x4000000 => 5,
                < 0x80000000 => 6,
                _ => 7
            };
            Write((0xff << (8 - length)) | (int)(value >> (6 * (length - 1))), 8);
            for (int i = length - 2; i >= 0; i--)
            {
                Write(0x80 | ((value >> (6 * i)) & 63), 8);
            }
        }

        private static int Crc(IEnumerable<byte> data, int width, int polynomial)
        {
            int result = 0;
            foreach (byte value in data)
            {
                for (int i = 7; i >= 0; i--)
                {
                    bool bit = ((result >> (width - 1)) ^ (value >> i)) % 2 != 0;
                    result = (result << 1) & ((1 << width) - 1);
                    if (bit)
                    {
                        result ^= polynomial;
                    }
                }
            }
            return result;
        }
    }
}
