// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1Configuration(Av1SequenceHeader? Sequence, bool Monochrome, int ChromaPosition, int Level, bool Tier)
    {
        internal static Av1Configuration Read(ReadOnlySpan<byte> encoded)
        {
            Av1CodecConfigurationRecord record = Av1CodecConfigurationRecord.Read(encoded);
            if (record.Profile != 0 || record.BitDepth != 8 || !record.SubsamplingX || !record.SubsamplingY)
            {
                throw new NotSupportedException("AV1 reconstruction requires Main-profile eight-bit 4:2:0 or monochrome samples.");
            }
            Av1SequenceHeader? sequence = null;
            Av1ObuReader obus = new(encoded[Av1CodecConfigurationRecord.HeaderBytes..], allowImplicitFinalSize: false);
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
            Av1Configuration configuration = new(sequence, record.Monochrome, record.ChromaPosition, record.Level, record.HighTier);
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
