// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal static class HevcSliceHeaders
    {
        public static PicturePartitions Partition(IReadOnlyList<HevcSliceHeader> slices)
        {
            if (slices.Count == 0) { throw new InvalidDataException("HEVC access unit has no picture slices."); }
            HevcSliceHeader first = slices[0];
            foreach (HevcSliceHeader slice in slices)
            {
                if (!ReferenceEquals(slice.Picture, first.Picture) || slice.PictureOrderCount != first.PictureOrderCount ||
                    slice.NalType != first.NalType || slice.TemporalId != first.TemporalId || slice.PictureOutput != first.PictureOutput ||
                    slice.NoOutputOfPriorPictures != first.NoOutputOfPriorPictures || slice.TemporalMvp != first.TemporalMvp ||
                    !slice.References.ShortTerm.Pictures.SequenceEqual(first.References.ShortTerm.Pictures) ||
                    !slice.References.LongTerm.SequenceEqual(first.References.LongTerm))
                { throw new InvalidDataException("Inconsistent HEVC slices in one picture."); }
            }
            return new(slices);
        }
    }
}
