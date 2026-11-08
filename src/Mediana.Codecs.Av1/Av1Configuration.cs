// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1Configuration(Av1SequenceHeader? Sequence, bool Monochrome, int ChromaPosition, int Level, bool Tier)
    {
        internal static Av1Configuration Read(ReadOnlySpan<byte> encoded)
        {
            if (encoded.Length < 4 || encoded[0] != 0x81 || (encoded[3] & 0xE0) != 0 || ((encoded[3] & 0x10) == 0 && (encoded[3] & 15) != 0))
            {
                throw new InvalidDataException("The AV1 codec configuration record is invalid.");
            }
            if ((encoded[1] >> 5) != 0 || (encoded[2] & 0x60) != 0 || (encoded[2] & 12) != 12)
            {
                throw new NotSupportedException("AV1 reconstruction requires Main-profile eight-bit 4:2:0 or monochrome samples.");
            }
            int level = encoded[1] & 31;
            bool tier = (encoded[2] & 128) != 0;
            bool monochrome = (encoded[2] & 16) != 0;
            int chromaPosition = encoded[2] & 3;
            if ((level <= 7 && tier) || chromaPosition == 3 || (monochrome && chromaPosition != 0))
            {
                throw new InvalidDataException("The AV1 codec configuration signal is invalid.");
            }
            Av1SequenceHeader? sequence = null;
            Av1ObuReader obus = new(encoded[4..]);
            int count = 0;
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                if (type == 1)
                {
                    if (count != 0)
                    {
                        throw new InvalidDataException("The AV1 configuration sequence header must be its first and only sequence OBU.");
                    }
                    sequence = Av1SequenceHeader.Parse(payload);
                }
                else if (type != 5 && type != 15 && type != 0 && type is not (>= 9 and <= 14))
                {
                    throw new NotSupportedException("The AV1 configuration contains an unsupported non-metadata OBU.");
                }
                count++;
            }
            Av1Configuration configuration = new(sequence, monochrome, chromaPosition, level, tier);
            if (sequence is Av1SequenceHeader header)
            {
                configuration.Validate(header);
            }
            return configuration;
        }

        internal void Validate(Av1SequenceHeader sequence)
        {
            if (sequence.Monochrome != Monochrome || sequence.ChromaPosition != ChromaPosition || sequence.Tools.Level != Level || sequence.Tools.Tier != Tier)
            {
                throw new InvalidDataException("The AV1 sequence header does not match its codec configuration record.");
            }
        }
    }
}
