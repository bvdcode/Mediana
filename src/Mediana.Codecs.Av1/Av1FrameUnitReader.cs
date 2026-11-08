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
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                cancellationToken.ThrowIfCancellationRequested();
                sequence |= type == 1;
                if (type == 6 || (type == 3 && !payload.IsEmpty && (payload[0] & 128) != 0))
                {
                    unit = data[offset..obus.ByteOffset];
                    offset = obus.ByteOffset;
                    return true;
                }
                if (type is 3 or 4 or 7 or 8)
                {
                    throw new NotSupportedException("AV1 separate frame headers, tile groups and tile lists are not implemented.");
                }
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
