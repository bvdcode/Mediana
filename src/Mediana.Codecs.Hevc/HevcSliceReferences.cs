// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal record HevcSliceReferences(int PictureOrderCountLsb, ShortTermReferenceSet ShortTerm,
        IReadOnlyList<HevcLongTermReference> LongTerm)
    {
        public static HevcSliceReferences Empty { get; } = new(0, ShortTermReferenceSet.Empty, []);
        public int UsedCount => ShortTerm.Pictures.Count(picture => picture.UsedByCurrentPicture) +
            LongTerm.Count(picture => picture.UsedByCurrentPicture);
    }
}
