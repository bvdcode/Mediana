// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264PictureParser
    {
        public static H264PictureParameterSet Parse(ReadOnlySpan<byte> nal,
            IReadOnlyDictionary<int, H264SequenceParameterSet> sequences)
        {
            H264BitReader bits = H264Nal.Payload(nal, 8);
            int id = bits.ReadUe(255);
            int sequenceId = bits.ReadUe(31);
            if (!sequences.ContainsKey(sequenceId))
            {
                throw new InvalidDataException("The H.264 picture parameter set references a missing sequence.");
            }
            bool cabac = bits.ReadFlag();
            bool bottomOrder = bits.ReadFlag();
            if (bits.ReadUe(7) != 0)
            {
                throw new NotSupportedException("H.264 slice groups are not supported.");
            }
            int referencesL0 = bits.ReadUe(31) + 1;
            int referencesL1 = bits.ReadUe(31) + 1;
            bool weighted = bits.ReadFlag();
            int biWeighted = (int)bits.ReadBits(2);
            if (biWeighted == 3)
            {
                throw new InvalidDataException("The H.264 weighted biprediction mode is invalid.");
            }
            int initialQp = bits.ReadSe(-26, 25) + 26;
            bits.ReadSe(-26, 25);
            int chromaOffset = bits.ReadSe(-12, 12);
            bool deblocking = bits.ReadFlag();
            bool constrained = bits.ReadFlag();
            bool redundant = bits.ReadFlag();
            bool transform8 = false;
            int secondOffset = chromaOffset;
            if (bits.MoreRbspData())
            {
                transform8 = bits.ReadFlag();
                if (bits.ReadFlag())
                {
                    throw new NotSupportedException("H.264 picture scaling matrices are not supported.");
                }
                secondOffset = bits.ReadSe(-12, 12);
            }
            bits.ReadTrailingBits();
            return new H264PictureParameterSet(id, sequenceId, cabac, bottomOrder, initialQp,
                chromaOffset, secondOffset, deblocking, constrained, redundant, transform8,
                referencesL0, referencesL1, weighted, biWeighted);
        }
    }
}
