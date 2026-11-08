// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs.Av1
{
    internal class Av1RandomAccessInspector(int streamIndex, Av1Configuration configuration) : IRandomAccessInspector
    {
        private readonly Av1ReferencePictures emptyReferences = new();

        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet belongs to a different AV1 stream.", nameof(packet));
            }
            Av1SequenceHeader? sequence = configuration.Sequence;
            Av1ObuReader obus = new(packet.Data.Span);
            bool hasSequence = false;
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (type)
                {
                    case 1:
                        if (hasSequence)
                        {
                            throw new InvalidDataException("The AV1 access unit contains multiple sequence headers.");
                        }
                        sequence = Av1SequenceHeader.Parse(payload);
                        configuration.Validate(sequence.Value);
                        hasSequence = true;
                        break;
                    case 3:
                    case 6:
                        if (payload.IsEmpty)
                        {
                            throw new InvalidDataException("The AV1 frame OBU is empty.");
                        }
                        if (sequence is not Av1SequenceHeader geometry) { return false; }
                        Av1BitReader bits = new(payload);
                        if (!geometry.Tools.ReducedStillPicture)
                        {
                            if (bits.Flag())
                            {
                                if (type == 6)
                                {
                                    throw new InvalidDataException("An AV1 frame OBU must not use show-existing syntax.");
                                }
                                return false;
                            }
                            if ((Av1FrameType)bits.Read(2) != Av1FrameType.Key) { return false; }
                        }
                        if (type == 3)
                        {
                            throw new NotSupportedException("AV1 separate frame headers and tile groups are not implemented.");
                        }
                        bits = new(payload);
                        Av1FrameContext context = Av1FrameContext.Read(ref bits, geometry, emptyReferences);
                        return context.RefreshReferences == Av1ReferencePictures.All;
                    case 4:
                    case 7:
                    case 8:
                        throw new NotSupportedException("AV1 separate tile groups and tile lists are not implemented.");
                }
            }
            throw new InvalidDataException("The AV1 access unit contains no frame.");
        }
    }
}
