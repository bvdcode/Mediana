// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ffv1
{
    internal static class Ffv1Crc
    {
        private static readonly uint[] Table = CreateTable();

        public static bool IsValid(ReadOnlySpan<byte> data)
        {
            uint remainder = 0;
            foreach (byte value in data)
            {
                remainder = (remainder << 8) ^ Table[(remainder >> 24) ^ value];
            }
            return remainder == 0;
        }

        private static uint[] CreateTable()
        {
            uint[] result = new uint[256];
            for (uint index = 0; index < result.Length; index++)
            {
                uint remainder = index << 24;
                for (int bit = 0; bit < 8; bit++)
                {
                    remainder = (remainder << 1) ^ ((remainder & 0x8000_0000) != 0 ? 0x04c11db7U : 0);
                }
                result[index] = remainder;
            }
            return result;
        }
    }
}
