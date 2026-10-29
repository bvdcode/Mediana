// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    internal class ReferencePictureSyntax(int log2Poc, ShortTermReferenceSet[] shortTermSets,
        bool longTermPresent, int longTermCount)
    {
        public static ReferencePictureSyntax Read(BitReader reader, int log2Poc)
        {
            int count = HevcSyntax.Ue(reader, 64, "num_short_term_ref_pic_sets");
            ShortTermReferenceSet[] sets = new ShortTermReferenceSet[count];
            for (int i = 0; i < count; i++)
            {
                sets[i] = ShortTermReferenceSet.Read(reader, sets, i, false);
            }

            bool longTermPresent = reader.ReadFlag();
            int longTermCount = longTermPresent ? HevcSyntax.Ue(reader, 32, "num_long_term_ref_pics_sps") : 0;
            for (int i = 0; i < longTermCount; i++)
            {
                reader.SkipBits(log2Poc + 1);
            }
            return new ReferencePictureSyntax(log2Poc, sets, longTermPresent, longTermCount);
        }

        public int ReadIntraSlice(BitReader reader)
        {
            int poc = reader.Read(log2Poc);
            bool fromSequence = reader.ReadFlag();
            if (fromSequence)
            {
                if (shortTermSets.Length == 0)
                {
                    throw new InvalidDataException("The slice selects a missing short-term reference picture set.");
                }
                ReadIndex(reader, shortTermSets.Length);
            }
            else
            {
                ShortTermReferenceSet.Read(reader, shortTermSets, shortTermSets.Length, true);
            }

            if (!longTermPresent)
            {
                return poc;
            }
            int sequenceCount = longTermCount > 0
                ? HevcSyntax.Ue(reader, longTermCount, "num_long_term_sps") : 0;
            int explicitCount = HevcSyntax.Ue(reader, 32 - sequenceCount, "num_long_term_pics");
            for (int i = 0; i < (sequenceCount + explicitCount); i++)
            {
                if (i < sequenceCount)
                {
                    ReadIndex(reader, longTermCount);
                }
                else
                {
                    reader.SkipBits(log2Poc);
                    reader.ReadFlag();
                }
                if (reader.ReadFlag())
                {
                    reader.ReadUev();
                }
            }
            return poc;
        }

        private static int ReadIndex(BitReader reader, int count)
        {
            int bits = 0;
            for (int maximum = count - 1; maximum > 0; maximum >>= 1)
            {
                bits++;
            }
            int index = reader.Read(bits);
            if (index >= count)
            {
                throw new InvalidDataException("Reference picture set index is out of range.");
            }
            return index;
        }
    }
}
