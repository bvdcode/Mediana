// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1
{
    internal partial class Av1PictureDecoder(MediaBufferAllocator allocator, Av1Configuration? configuration = null) : IDisposable
    {
        private readonly Av1ReferencePictures references = new();
        private Av1SequenceHeader? savedSequence = configuration?.Sequence;

        internal Av1Picture Decode(ReadOnlySpan<byte> encoded, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            Av1ObuReader obus = new(encoded);
            Av1SequenceHeader? sequence = savedSequence;
            bool hasSequence = false;
            ReadOnlySpan<byte> frame = default;
            bool separateHeader = false;
            bool hasGroups = false;
            int frameEnd = 0;
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (type)
                {
                    case 1:
                        if (hasSequence || !frame.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 access unit contains multiple sequence headers.");
                        }
                        sequence = Av1SequenceHeader.Parse(payload);
                        configuration?.Validate(sequence.Value);
                        hasSequence = true;
                        break;
                    case 2:
                        break;
                    case 3:
                    case 6:
                        if (sequence is null || !frame.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 access unit requires a sequence configuration and one frame.");
                        }
                        if (payload.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 frame OBU is empty.");
                        }
                        frame = payload;
                        separateHeader = type == 3;
                        frameEnd = obus.ByteOffset;
                        break;
                    case 4:
                        if (frame.IsEmpty || !separateHeader)
                        {
                            throw new InvalidDataException("An AV1 tile group requires a separate frame header.");
                        }
                        hasGroups = true;
                        break;
                    case 7:
                    case 8:
                        throw new NotSupportedException("AV1 redundant frame headers and tile lists are not implemented.");
                    case 0:
                    case 5:
                    case 9:
                    case 10:
                    case 11:
                    case 12:
                    case 13:
                    case 14:
                    case 15:
                        break;
                    default:
                        throw new InvalidDataException("The AV1 OBU type is invalid.");
                }
            }
            if (sequence is not Av1SequenceHeader geometry || frame.IsEmpty)
            {
                throw new InvalidDataException("The AV1 access unit is missing its sequence or frame.");
            }
            if (separateHeader && !geometry.Tools.ReducedStillPicture && (frame[0] & 128) != 0)
            {
                if (hasGroups || (hasSequence && sequence != savedSequence))
                {
                    throw new InvalidDataException("AV1 reference display requires an unchanged non-reduced sequence.");
                }
                Av1BitReader bits = new(frame);
                _ = bits.Flag();
                int reference = bits.Read(3);
                uint? presentationTime = geometry.Timing?.ReadPresentationTime(ref bits);
                if (geometry.Tools.FrameIdBits != 0 && bits.Read(geometry.Tools.FrameIdBits) != references.Get(reference).Header.FrameId)
                {
                    throw new InvalidDataException("The AV1 reference display identifier does not match.");
                }
                bits.Finish();
                return references.Show(reference, new(presentationTime, null));
            }
            Av1FrameHeader header = Av1FrameHeader.Parse(frame, geometry, references, separateHeader);
            ReadOnlySpan<byte> tileData = separateHeader ? encoded[frameEnd..] : frame[header.TileOffset..];
            Av1Picture picture = header.Geometry.BitDepth switch
            {
                8 => Reconstruct<byte>(header, tileData, separateHeader, cancellationToken),
                10 => Reconstruct<ushort>(header, tileData, separateHeader, cancellationToken),
                _ => throw new NotSupportedException("AV1 reconstruction requires eight- or ten-bit Main-profile samples.")
            };
            savedSequence = sequence;
            return picture;
        }
        public void Dispose() => references.Dispose();
    }
}
