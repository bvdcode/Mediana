// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO;

namespace Mediana.Codecs.Hevc
{
    internal class ReferencePictureSyntax(int log2Poc, ShortTermReferenceSet[] shortTermSets,
        bool longTermPresent, HevcLongTermReference[] longTermPictures)
    {
        public int Log2PictureOrderCount => log2Poc;

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
            HevcLongTermReference[] longTerm = new HevcLongTermReference[longTermCount];
            for (int i = 0; i < longTermCount; i++)
            {
                longTerm[i] = new(reader.Read(log2Poc), reader.ReadFlag());
            }
            return new ReferencePictureSyntax(log2Poc, sets, longTermPresent, longTerm);
        }

        public HevcSliceReferences ReadSlice(BitReader reader)
        {
            int poc = reader.Read(log2Poc);
            bool fromSequence = reader.ReadFlag();
            ShortTermReferenceSet shortTerm;
            if (fromSequence)
            {
                if (shortTermSets.Length == 0)
                {
                    throw new InvalidDataException("The slice selects a missing short-term reference picture set.");
                }
                shortTerm = shortTermSets[ReadIndex(reader, shortTermSets.Length)];
            }
            else
            {
                shortTerm = ShortTermReferenceSet.Read(reader, shortTermSets, shortTermSets.Length, true);
            }

            if (!longTermPresent)
            {
                return new(poc, shortTerm, []);
            }
            int sequenceCount = longTermPictures.Length > 0
                ? HevcSyntax.Ue(reader, longTermPictures.Length, "num_long_term_sps") : 0;
            int explicitCount = HevcSyntax.Ue(reader, 32 - sequenceCount, "num_long_term_pics");
            HevcLongTermReference[] longTerm = new HevcLongTermReference[sequenceCount + explicitCount];
            long previousCycle = 0;
            for (int i = 0; i < longTerm.Length; i++)
            {
                HevcLongTermReference picture;
                if (i < sequenceCount)
                {
                    picture = longTermPictures[ReadIndex(reader, longTermPictures.Length)];
                }
                else
                {
                    picture = new(reader.Read(log2Poc), reader.ReadFlag());
                }
                bool hasCycle = reader.ReadFlag();
                long cycle = hasCycle ? HevcSyntax.Ue(reader, 1 << (32 - log2Poc), "delta_poc_msb_cycle_lt") : 0;
                if (i != 0 && i != sequenceCount) { cycle += previousCycle; }
                longTerm[i] = picture with { HasMsbCycle = hasCycle, MsbCycle = cycle };
                previousCycle = cycle;
            }
            return new(poc, shortTerm, Array.AsReadOnly(longTerm));
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
