// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264ReferenceField(H264PictureIdentity FrameIdentity, int FrameNumber,
        H264PictureStructure Structure, int? LongTermIndex)
    {
        public bool Complements(H264SliceHeader header) =>
            H264FieldPairing.IsCandidate(Structure, FrameNumber, true, header) && !H264FieldPairing.ResetsReferences(header);
    }
}
