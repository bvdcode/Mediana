// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections.Generic;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    internal class ShortTermReferenceSet(long[] deltas)
    {
        public long[] Deltas { get; } = deltas;

        public static ShortTermReferenceSet Read(BitReader reader, ShortTermReferenceSet[] preceding, int index, bool slice)
        {
            if ((index > 0) && reader.ReadFlag())
            {
                return ReadPredicted(reader, preceding, index, slice);
            }

            int negative = HevcSyntax.Ue(reader, 16, "num_negative_pics");
            int positive = HevcSyntax.Ue(reader, 16 - negative, "num_positive_pics");
            long[] explicitDeltas = new long[negative + positive];
            long previous = 0;
            for (int i = 0; i < explicitDeltas.Length; i++)
            {
                if (i == negative)
                {
                    previous = 0;
                }
                int step = HevcSyntax.Ue(reader, 32767, "delta_poc_minus1") + 1;
                previous += i < negative ? -step : step;
                explicitDeltas[i] = previous;
                reader.ReadFlag();
            }
            return new ShortTermReferenceSet(explicitDeltas);
        }

        private static ShortTermReferenceSet ReadPredicted(BitReader reader, ShortTermReferenceSet[] preceding, int index, bool slice)
        {
            int distance = slice ? HevcSyntax.Ue(reader, index - 1, "delta_idx_minus1") + 1 : 1;
            ShortTermReferenceSet source = preceding[index - distance];
            int sign = reader.ReadFlag() ? -1 : 1;
            int delta = sign * (HevcSyntax.Ue(reader, 32767, "abs_delta_rps_minus1") + 1);
            List<long> entries = new List<long>();
            for (int i = 0; i <= source.Deltas.Length; i++)
            {
                bool used = reader.ReadFlag();
                bool include = used || reader.ReadFlag();
                long shifted = (i < source.Deltas.Length ? source.Deltas[i] : 0) + delta;
                if (include && (shifted != 0))
                {
                    entries.Add(shifted);
                }
            }
            if (entries.Count > 16)
            {
                throw new InvalidDataException("Too many reference pictures.");
            }
            entries.Sort((left, right) =>
            {
                if ((left < 0) && (right < 0))
                {
                    return right.CompareTo(left);
                }
                return left.CompareTo(right);
            });
            return new ShortTermReferenceSet(entries.ToArray());
        }
    }
}
