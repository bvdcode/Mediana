// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>Facts computed from a complete container packet index, without reading or decoding encoded payloads.</summary>
    public class ContainerPacketIndexInfo
    {
        public ContainerPacketIndexInfo(long packetCount, long payloadBytes, MediaDuration decodeDuration)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(packetCount);
            ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
            decodeDuration.TimeBase.Validate();
            PacketCount = packetCount;
            PayloadBytes = payloadBytes;
            DecodeDuration = decodeDuration;
        }

        public long PacketCount { get; }
        /// <summary>Total bytes of encoded packet payload, including its declared framing and excluding container overhead.</summary>
        public long PayloadBytes { get; }
        /// <summary>Sum of indexed decode durations. Excludes presentation offsets and edits; does not count decoded PCM or pictures.</summary>
        public MediaDuration DecodeDuration { get; }

        /// <summary>Approximate mean encoded payload bits per second over the complete indexed decode duration.
        /// Includes packet framing, excludes container overhead and presentation edits. Null for zero duration.</summary>
        public double? AveragePayloadBitRate => DecodeDuration.Value == 0 ? null :
            (double)PayloadBytes * 8 * DecodeDuration.TimeBase.Denominator /
            DecodeDuration.Value / DecodeDuration.TimeBase.Numerator;
    }
}
