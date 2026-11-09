// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;
using Mediana.Formats;

namespace Mediana.Containers.Adts
{
    internal static class AdtsPacketFactory
    {
        public static EncodedStreamFormat GetFormat(AdtsPacketFraming framing) => framing switch
        {
            AdtsPacketFraming.TransportFrame => StreamFormats.AacAdts,
            AdtsPacketFraming.RawAccessUnit => StreamFormats.Aac,
            _ => throw new ArgumentOutOfRangeException(nameof(framing))
        };

        public static MediaPacket Create(MediaBuffer buffer, bool hasCrc, MediaTimestamp timestamp, MediaDuration duration,
            AdtsPacketFraming framing)
        {
            if (framing == AdtsPacketFraming.TransportFrame)
            {
                _ = new AdtsFrameLayout(buffer.Memory.Span);
                return new(0, buffer, timestamp, timestamp, duration);
            }
            if (!hasCrc)
            {
                return new(0, buffer, timestamp, timestamp, duration);
            }
            using MediaBuffer payload = buffer.Slice(AdtsPacketProtection.PrefixByteLength,
                buffer.Memory.Length - AdtsPacketProtection.PrefixByteLength);
            using MediaBuffer prefix = buffer.Slice(0, AdtsPacketProtection.PrefixByteLength);
            using MediaPacketSideData protection = new(AdtsPacketProtection.SideDataKind, prefix);
            return new(0, payload, timestamp, timestamp, duration, sideData: [protection]);
        }
    }
}
