// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal static class HevcAccessUnitReader
    {
        public static HevcAccessUnit Read(ReadOnlyMemory<byte> data, HevcConfiguration configuration,
            HevcDecoderOptions options, CancellationToken cancellationToken)
        {
            List<HevcSliceHeader> slices = [];
            int count = 0;
            bool endSequence = false, endStream = false;
            foreach (NalPayload nal in NalPayload.ReadLengthPrefixed(data, configuration.LengthSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > options.MaxNalsPerPacket) { throw new NotSupportedException("HEVC packet exceeds its configured NAL count limit."); }
                if (endStream) { throw new InvalidDataException("HEVC packet contains NAL units after end of bitstream."); }
                switch (nal.Type)
                {
                    case >= 0 and <= 9 or >= 16 and <= 21:
                        if (endSequence) { throw new InvalidDataException("HEVC packet contains pictures after end of sequence."); }
                        HevcSliceHeader slice = HevcSliceHeader.Read(nal, configuration.Pictures);
                        if (slices.Count != 0 && slice.StartCtbAddress == 0)
                        { throw new InvalidDataException("HEVC packet contains more than one picture."); }
                        slices.Add(slice); break;
                    case 33 or 34: configuration = configuration.WithParameter(nal, options); break;
                    case 36 or 37:
                        HevcSyntax.TrailingBits(HevcSyntax.Open(nal, nal.Type));
                        if (nal.Type == 36) { endSequence = true; }
                        else { endStream = true; }
                        break;
                    case 32 or 35 or 38 or 39 or 40: break;
                    default: throw new UnsupportedHevcFeatureException("HEVC NAL unit type " + nal.Type);
                }
            }
            if (count == 0) { throw new InvalidDataException("HEVC access unit is empty."); }
            return new(configuration, slices.AsReadOnly(), endSequence, endStream);
        }
    }
}
