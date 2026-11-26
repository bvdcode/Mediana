// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>Reads AV1 low-overhead OBU boundaries without interpreting coded pictures.</summary>
    public ref struct Av1ObuReader(ReadOnlySpan<byte> encoded, bool allowImplicitFinalSize = false)
    {
        private readonly ReadOnlySpan<byte> data = encoded;
        private int offset;

        public readonly int ByteOffset => offset;
        public int TemporalId { get; private set; }
        public int SpatialId { get; private set; }

        public bool TryRead(out int type, out ReadOnlySpan<byte> payload) =>
            TryReadCore(false, out type, out payload, out _);

        /// <summary>Reads an OBU from a packet prefix and reports whether its explicit size exceeded the available bytes.</summary>
        public bool TryReadPrefix(out int type, out ReadOnlySpan<byte> payload, out bool complete) =>
            TryReadCore(true, out type, out payload, out complete);

        private bool TryReadCore(bool allowTruncatedPayload, out int type, out ReadOnlySpan<byte> payload, out bool complete)
        {
            type = 0;
            payload = default;
            complete = true;
            if (offset == data.Length) { return false; }
            int header = data[offset++];
            if ((header & 0x81) != 0)
            {
                throw new InvalidDataException("The AV1 OBU forbidden or reserved bit is set.");
            }
            type = (header >> 3) & 15;
            TemporalId = 0;
            SpatialId = 0;
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
                TemporalId = extension >> 5;
                SpatialId = (extension >> 3) & 3;
            }
            int length;
            if ((header & 2) == 0)
            {
                if (!allowImplicitFinalSize)
                {
                    throw new InvalidDataException("This AV1 OBU requires an explicit payload size.");
                }
                length = data.Length - offset;
            }
            else
            {
                uint size = ReadSize();
                complete = size <= (uint)(data.Length - offset);
                if (!complete && !allowTruncatedPayload)
                {
                    throw new InvalidDataException("The AV1 OBU extent is invalid.");
                }
                length = (int)Math.Min(size, (uint)(data.Length - offset));
                if (type == 2 && size != 0)
                {
                    throw new InvalidDataException("An AV1 temporal delimiter must be empty.");
                }
            }
            payload = data.Slice(offset, length);
            offset += length;
            if (type == 2 && !payload.IsEmpty)
            {
                throw new InvalidDataException("An AV1 temporal delimiter must be empty.");
            }
            return true;
        }

        private uint ReadSize()
        {
            ulong size = 0;
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
                    if (size > uint.MaxValue)
                    {
                        throw new InvalidDataException("The AV1 OBU extent is invalid.");
                    }
                    return (uint)size;
                }
            }
            throw new InvalidDataException("The AV1 OBU size exceeds eight bytes.");
        }
    }
}
