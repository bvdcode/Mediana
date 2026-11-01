// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats
{
    internal static class IeeeCrc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(ReadOnlySpan<byte> data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint crc = uint.MaxValue;
            for (int index = 0; index < data.Length; index++)
            {
                if ((index & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                crc = Table[(crc ^ data[index]) & 255] ^ (crc >> 8);
            }
            return ~crc;
        }

        private static uint[] CreateTable()
        {
            uint[] result = new uint[256];
            for (uint index = 0; index < result.Length; index++)
            {
                uint value = index;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xedb88320u;
                }
                result[index] = value;
            }
            return result;
        }
    }
}
