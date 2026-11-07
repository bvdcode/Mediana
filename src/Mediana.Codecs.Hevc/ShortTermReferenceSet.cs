// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Hevc
{
    internal class ShortTermReferenceSet(HevcShortTermReference[] pictures)
    {
        public static ShortTermReferenceSet Empty { get; } = new([]);
        public IReadOnlyList<HevcShortTermReference> Pictures { get; } = Array.AsReadOnly(pictures);

        public static ShortTermReferenceSet Read(BitReader reader, ShortTermReferenceSet[] preceding, int index, bool slice)
        {
            if ((index > 0) && reader.ReadFlag())
            {
                return ReadPredicted(reader, preceding, index, slice);
            }

            int negative = HevcSyntax.Ue(reader, 16, "num_negative_pics");
            int positive = HevcSyntax.Ue(reader, 16 - negative, "num_positive_pics");
            HevcShortTermReference[] explicitPictures = new HevcShortTermReference[negative + positive];
            long previous = 0;
            for (int i = 0; i < explicitPictures.Length; i++)
            {
                if (i == negative)
                {
                    previous = 0;
                }
                int step = HevcSyntax.Ue(reader, 32767, "delta_poc_minus1") + 1;
                previous += i < negative ? -step : step;
                explicitPictures[i] = new(previous, reader.ReadFlag());
            }
            return new ShortTermReferenceSet(explicitPictures);
        }

        private static ShortTermReferenceSet ReadPredicted(BitReader reader, ShortTermReferenceSet[] preceding, int index, bool slice)
        {
            int distance = slice ? HevcSyntax.Ue(reader, index - 1, "delta_idx_minus1") + 1 : 1;
            ShortTermReferenceSet source = preceding[index - distance];
            int sign = reader.ReadFlag() ? -1 : 1;
            int delta = sign * (HevcSyntax.Ue(reader, 32767, "abs_delta_rps_minus1") + 1);
            List<HevcShortTermReference> entries = new List<HevcShortTermReference>();
            for (int i = 0; i <= source.Pictures.Count; i++)
            {
                bool used = reader.ReadFlag();
                bool include = used || reader.ReadFlag();
                long shifted = (i < source.Pictures.Count ? source.Pictures[i].Delta : 0) + delta;
                if (include && (shifted != 0))
                {
                    entries.Add(new(shifted, used));
                }
            }
            if (entries.Count > 16)
            {
                throw new InvalidDataException("Too many reference pictures.");
            }
            entries.Sort((left, right) =>
            {
                if ((left.Delta < 0) && (right.Delta < 0))
                {
                    return right.Delta.CompareTo(left.Delta);
                }
                return left.Delta.CompareTo(right.Delta);
            });
            return new ShortTermReferenceSet(entries.ToArray());
        }
    }
}
