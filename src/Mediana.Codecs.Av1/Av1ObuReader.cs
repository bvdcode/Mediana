// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref struct Av1ObuReader(ReadOnlySpan<byte> encoded)
    {
        private readonly ReadOnlySpan<byte> data = encoded;
        private int offset;

        internal int ByteOffset => offset;

        internal bool TryRead(out int type, out ReadOnlySpan<byte> payload)
        {
            type = 0;
            payload = default;
            if (offset == data.Length)
            {
                return false;
            }
            int header = data[offset++];
            if ((header & 0x81) != 0)
            {
                throw new InvalidDataException("The AV1 OBU forbidden or reserved bit is set.");
            }
            type = (header >> 3) & 15;
            if ((header & 4) != 0)
            {
                if (offset == data.Length)
                {
                    throw new InvalidDataException("The AV1 OBU extension header is truncated.");
                }
                int extension = data[offset++];
                if ((extension & 7) != 0)
                {
                    throw new InvalidDataException("The AV1 OBU extension reserved bits are set.");
                }
                if (extension != 0)
                {
                    throw new NotSupportedException("AV1 temporal and spatial scalability are not implemented.");
                }
            }
            if ((header & 2) == 0)
            {
                throw new NotSupportedException("AV1 input requires low-overhead OBUs with explicit sizes.");
            }
            ulong size = 0;
            bool terminated = false;
            for (int index = 0; index < 8; index++)
            {
                if (offset == data.Length)
                {
                    throw new InvalidDataException("The AV1 OBU size is truncated.");
                }
                byte next = data[offset++];
                size |= (ulong)(next & 127) << (index * 7);
                if ((next & 128) == 0)
                {
                    terminated = true;
                    break;
                }
            }
            if (!terminated || size > uint.MaxValue || size > (ulong)(data.Length - offset))
            {
                throw new InvalidDataException("The AV1 OBU extent is invalid.");
            }
            payload = data.Slice(offset, (int)size);
            offset += (int)size;
            return true;
        }
    }
}
