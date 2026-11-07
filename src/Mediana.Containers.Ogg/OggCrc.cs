// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Ogg
{
    internal static class OggCrc
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(ReadOnlySpan<byte> page)
        {
            uint crc = 0;
            for (int index = 0; index < page.Length; index++)
            {
                byte value = index is >= 22 and < 26 ? (byte)0 : page[index];
                crc = (crc << 8) ^ Table[(byte)(crc >> 24) ^ value];
            }
            return crc;
        }

        private static uint[] CreateTable()
        {
            uint[] table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                uint crc = index << 24;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04c11db7 : crc << 1;
                }
                table[index] = crc;
            }
            return table;
        }
    }
}
