// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal static class AdtsCrc16
    {
        public const ushort Initial = ushort.MaxValue;

        public static ushort Calculate(ReadOnlySpan<byte> data)
        {
            ushort remainder = Initial;
            foreach (byte value in data)
            {
                remainder = Append(remainder, value, 8);
            }
            return remainder;
        }

        public static ushort Append(ushort remainder, int value, int count)
        {
            for (int shift = count - 1; shift >= 0; shift--)
            {
                int feedback = (remainder >> 15) ^ ((value >> shift) & 1);
                remainder = (ushort)(remainder << 1);
                if (feedback != 0)
                {
                    remainder ^= 0x8005;
                }
            }
            return remainder;
        }
    }
}
