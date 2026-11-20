// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264ReferenceField(H264PictureIdentity FrameIdentity, int FrameNumber,
        H264PictureStructure Structure, int? LongTermIndex)
    {
        public bool Complements(H264SliceHeader header) => !header.IsIdr && header.Layout.IsField &&
            header.Structure != Structure && header.FrameNumber == FrameNumber && header.ReferenceIndicator != 0 &&
            !header.MemoryOperations.Any(operation => operation.Operation == 5);
    }
}
