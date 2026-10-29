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
            ReadOnlyMemory<byte> slice = default;
            bool seenNal = false;
            bool endSequence = false;
            bool endStream = false;
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
                        if (!slice.IsEmpty)
                        {
                            throw new NotSupportedException("Each AVC packet must contain one complete single-slice picture.");
                        }
                        slice = nal;
                        break;
                    case 6:
                    case 7:
                    case 8:
                    case 9:
                        if (!slice.IsEmpty || (type == 9 && seenNal))
                        {
                            throw new InvalidDataException("The AVC packet crosses an access unit boundary.");
                        }
                        if (type is 7 or 8)
                        {
                            configuration = configuration.WithParameterSet(nal.Span, options);
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
            return new H264AccessUnit(configuration, slice, endSequence, endStream);
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
                case 6:
                    do
                    {
                        ReadExtended(ref bits, int.MaxValue);
                        int length = ReadExtended(ref bits, nal.Length);
                        for (int index = 0; index < length; index++)
                        {
                            if ((index & 255) == 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                            bits.ReadBits(8);
                        }
                    } while (bits.MoreRbspData());
                    break;
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

        private static int ReadExtended(ref H264BitReader bits, int maximum)
        {
            int value = 0;
            int next;
            do
            {
                next = (int)bits.ReadBits(8);
                if (next > maximum - value)
                {
                    throw new InvalidDataException("The H.264 SEI field exceeds its permitted length.");
                }
                value += next;
            } while (next == 255);
            return value;
        }
    }
}
