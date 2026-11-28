// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264AccessUnitReader
    {
        public static H264AccessUnit Read(ReadOnlyMemory<byte> packet, H264Configuration configuration,
            H264DecoderOptions options, CancellationToken cancellationToken = default)
        {
            if (packet.IsEmpty)
            {
                throw new InvalidDataException("An AVC access unit cannot be empty.");
            }
            H264NalReader reader = new(packet, configuration.LengthSize, options.MaxNalsPerPacket);
            List<ReadOnlyMemory<byte>> slices = [];
            int previousMacroblock = -1;
            bool seenNal = false;
            bool endSequence = false;
            bool endStream = false;
            ReadOnlyMemory<byte> pictureTimingNal = default;
            while (reader.TryRead(out ReadOnlyMemory<byte> nal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int type = H264Nal.Type(nal.Span);
                if (endStream || (endSequence && type != 11))
                {
                    throw new InvalidDataException("Unexpected H.264 data follows an end marker.");
                }
                switch (type)
                {
                    case 1:
                    case 5:
                        H264BitReader sliceBits = H264Nal.Payload(nal.Span, type);
                        int firstMacroblock = sliceBits.ReadUe();
                        if (firstMacroblock <= previousMacroblock)
                        {
                            throw new InvalidDataException("AVC slice macroblock ranges must be strictly increasing within one picture.");
                        }
                        if (slices.Count == 0 && firstMacroblock != 0)
                        {
                            throw new NotSupportedException("AVC pictures must start at macroblock zero; arbitrary slice order is not supported.");
                        }
                        previousMacroblock = firstMacroblock;
                        slices.Add(nal);
                        break;
                    case 6:
                    case 7:
                    case 8:
                    case 9:
                        if (slices.Count != 0 || (type == 9 && seenNal))
                        {
                            throw new InvalidDataException("The AVC packet crosses an access unit boundary.");
                        }
                        if (type is 7 or 8)
                        {
                            configuration = configuration.WithParameterSet(nal.Span, options.HeaderLimits);
                        }
                        else if (type == 6)
                        {
                            if (H264SeiReader.Validate(nal.Span, cancellationToken))
                            {
                                if (!pictureTimingNal.IsEmpty)
                                {
                                    throw new InvalidDataException("An H.264 access unit contains multiple picture-timing messages.");
                                }
                                pictureTimingNal = nal;
                            }
                        }
                        else
                        {
                            ReadAuxiliary(nal.Span, type, cancellationToken);
                        }
                        break;
                    case 10:
                    case 11:
                        if (nal.Length != 1 || (nal.Span[0] & 0x60) != 0)
                        {
                            throw new InvalidDataException("The H.264 end marker is invalid.");
                        }
                        endSequence = true;
                        endStream = type == 11;
                        break;
                    case 12:
                        ReadAuxiliary(nal.Span, type, cancellationToken);
                        break;
                    default:
                        throw new NotSupportedException($"H.264 NAL type {type} is not supported.");
                }
                seenNal = true;
            }
            return new H264AccessUnit(configuration, slices, endSequence, endStream, pictureTimingNal);
        }

        private static void ReadAuxiliary(ReadOnlySpan<byte> nal, int type, CancellationToken cancellationToken)
        {
            if ((nal[0] & 0x60) != 0)
            {
                throw new InvalidDataException("An H.264 auxiliary NAL must have a zero reference indicator.");
            }
            H264BitReader bits = H264Nal.Payload(nal, type);
            switch (type)
            {
                case 9:
                    bits.ReadBits(3);
                    break;
                case 12:
                    while (bits.MoreRbspData())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (bits.ReadBits(8) != 255)
                        {
                            throw new InvalidDataException("The H.264 filler byte is invalid.");
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unexpected H.264 auxiliary NAL type.");
            }
            bits.ReadTrailingBits();
        }
    }
}
