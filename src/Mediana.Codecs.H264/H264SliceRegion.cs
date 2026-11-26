// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal record H264SliceRegion(H264SliceHeader Header, int EndMacroblock,
        ReadOnlyMemory<byte> Payload = default, H264BitReaderCheckpoint PayloadCheckpoint = default)
    {
        public H264BitReader CreatePayloadReader() => new(Payload.Span, PayloadCheckpoint);
    }
}
