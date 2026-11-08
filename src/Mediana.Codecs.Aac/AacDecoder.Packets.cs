// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Aac
{
    internal partial class AacDecoder
    {
        private void ReadPacket(MediaPacket packet, CancellationToken cancellationToken)
        {
            bool discard = prerollPackets != 0;
            if (adts)
            {
                AacAdtsBlockReader blocks = new(packet.Data.Span, configuration, maxAccessUnitBytes);
                while (blocks.Index < blocks.BlockCount)
                {
                    ReadOnlySpan<byte> data = blocks.Read(out ReadOnlySpan<byte> protection, out ushort? crc, out bool trailing);
                    MediaTimestamp? timestamp = BlockTimestamp(packet.PresentationTime, blocks.Index);
                    int byteLength = DecodeAccessUnit(data, timestamp, discard, cancellationToken, protection, trailing, crc);
                    blocks.Advance(byteLength);
                }
            }
            else
            {
                if (packet.Data.Length > maxAccessUnitBytes)
                {
                    throw new NotSupportedException("The AAC packet exceeds the configured access unit limit.");
                }
                DecodeAccessUnit(packet.Data.Span, packet.PresentationTime, discard, cancellationToken,
                    AacPacketProtection.GetPrefix(packet));
            }
            if (discard)
            {
                prerollPackets--;
            }
        }

        private MediaTimestamp? BlockTimestamp(MediaTimestamp? timestamp, int index)
        {
            if (!timestamp.HasValue || index == 0)
            {
                return timestamp;
            }
            TimeBase coreClock = new(1, configuration.SampleRate);
            TimeBase clock = TimeBase.Common(timestamp.Value.TimeBase, coreClock);
            MediaTimestamp start = timestamp.Value.Rescale(clock, TimestampRounding.TowardZero);
            MediaTimestamp offset = new MediaTimestamp(index * 1024, coreClock).Rescale(clock, TimestampRounding.TowardZero);
            return new MediaTimestamp(checked(start.Value + offset.Value), clock);
        }
    }
}
