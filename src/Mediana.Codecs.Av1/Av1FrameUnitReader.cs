// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref struct Av1FrameUnitReader(ReadOnlySpan<byte> encoded)
    {
        private readonly ReadOnlySpan<byte> data = encoded;
        private Av1ObuReader obus = new(encoded);
        private int offset;

        internal bool TryRead(out ReadOnlySpan<byte> unit, CancellationToken cancellationToken)
        {
            bool sequence = false;
            bool header = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Av1ObuReader next = obus;
                if (!next.TryRead(out int type, out _)) { break; }
                if (header && type is 1 or 2 or 3 or 6)
                {
                    unit = data[offset..obus.ByteOffset];
                    offset = obus.ByteOffset;
                    return true;
                }
                obus = next;
                sequence |= type == 1;
                if (type == 3) { header = true; }
                if (type == 6)
                {
                    unit = data[offset..obus.ByteOffset];
                    offset = obus.ByteOffset;
                    return true;
                }
                if (type == 4 && !header)
                {
                    throw new InvalidDataException("An AV1 tile group requires a preceding frame header.");
                }
                if (type is 7 or 8)
                {
                    throw new NotSupportedException("AV1 redundant frame headers and tile lists are not implemented.");
                }
            }
            if (header)
            {
                unit = data[offset..obus.ByteOffset];
                offset = obus.ByteOffset;
                return true;
            }
            if (sequence)
            {
                throw new NotSupportedException("AV1 sequence-only access units are not implemented.");
            }
            unit = default;
            return false;
        }
    }
}
