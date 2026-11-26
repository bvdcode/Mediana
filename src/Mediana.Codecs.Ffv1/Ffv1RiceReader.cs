// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ffv1
{
    internal ref struct Ffv1RiceReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> bytes = data;
        private long position;

        internal bool ReadBit() => Read(1) != 0;

        internal int Read(int width)
        {
            if (width > 24 || width > (long)bytes.Length * 8 - position)
            {
                throw new InvalidDataException("The FFV1 Golomb-Rice payload is truncated or has an invalid integer width.");
            }
            int value = 0;
            for (int i = 0; i < width; i++, position++)
            {
                value = (value << 1) | ((bytes[(int)(position >> 3)] >> (7 - (int)(position & 7))) & 1);
            }
            return value;
        }

        internal int ReadSigned(int k)
        {
            int prefix = 0;
            while (prefix < 12 && !ReadBit())
            {
                prefix++;
            }
            int mapped = prefix == 12 ? 11 + Read(8) : (prefix << k) + Read(k);
            return (mapped & 1) == 0 ? mapped >> 1 : -1 - (mapped >> 1);
        }
    }
}
