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
            Av1FrameUnitReader units = new(packet.Data.Span);
            if (!units.TryRead(out ReadOnlySpan<byte> unit, cancellationToken))
            {
                throw new InvalidDataException("The AV1 access unit contains no frame.");
            }
            Av1ObuReader obus = new(unit);
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
                        Av1FrameHeader header = Av1FrameHeader.Parse(payload, geometry, emptyReferences, type == 3);
                        ReadOnlySpan<byte> tileData = type == 3 ? unit[obus.ByteOffset..] : payload[header.TileOffset..];
                        Av1TilePayloadReader tiles = new(tileData, header.Tiles, type == 6);
                        while (tiles.TryRead(out _, out _, cancellationToken))
                        {
                        }
                        return header.RefreshReferences == Av1ReferencePictures.All;
                    case 4:
                        throw new InvalidDataException("An AV1 tile group requires a preceding frame header.");
                    case 7:
                    case 8:
                        throw new NotSupportedException("AV1 redundant frame headers and tile lists are not implemented.");
                }
            }
            throw new InvalidDataException("The AV1 access unit contains no frame.");
        }
    }
}
