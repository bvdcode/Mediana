// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref struct Av1ObuReader(ReadOnlySpan<byte> encoded, bool allowImplicitFinalSize = true)
    {
        private Formats.Video.Av1ObuReader reader = new(encoded, allowImplicitFinalSize);

        internal readonly int ByteOffset => reader.ByteOffset;

        internal bool TryRead(out int type, out ReadOnlySpan<byte> payload)
        {
            bool available = reader.TryRead(out type, out payload);
            if (available && (reader.TemporalId != 0 || reader.SpatialId != 0))
            {
                throw new NotSupportedException("AV1 temporal and spatial scalability are not implemented.");
            }
            return available;
        }
    }
}
