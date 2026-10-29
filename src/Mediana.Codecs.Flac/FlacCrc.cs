// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Flac
{
    internal static class FlacCrc
    {
        public static byte Header(ReadOnlySpan<byte> data)
        {
            int crc = 0;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = ((crc << 1) ^ ((crc & 0x80) != 0 ? 0x07 : 0)) & 0xff;
                }
            }
            return (byte)crc;
        }

        public static ushort Frame(ReadOnlySpan<byte> data, CancellationToken cancellationToken)
        {
            int crc = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                crc ^= data[i] << 8;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = ((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x8005 : 0)) & 0xffff;
                }
            }
            return (ushort)crc;
        }
    }
}
