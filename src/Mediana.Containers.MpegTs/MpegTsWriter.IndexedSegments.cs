// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Containers.MpegTs
{
    public partial class MpegTsWriter
    {
        /// <summary>Creates a segment whose populated PID counters start at zero and finish at fifteen.
        /// Each track must mark its final packet. Adaptation stuffing and repeated program tables complete
        /// the counter cycles without changing elementary payloads or timestamps. Adjacent segments can
        /// be generated independently and concatenated without continuity-counter discontinuities.
        /// Supply the preceding segment's final packet and clock-track decode timestamps to bridge PCR.</summary>
        public static MpegTsWriter CreateIndexedSegment(Stream destination, IReadOnlyList<EncodedStreamInfo> streams,
            MediaBufferAllocator allocator, MpegTsSegmentTimeline? precedingTimeline = null, MpegTsWriteOptions? options = null)
        {
            MpegTsWriter writer = Create(destination, streams, allocator, options);
            bool success = false;
            try
            {
                writer.alignedSegment = true;
                if (precedingTimeline is not null)
                {
                    long previous = writer.Timestamp(precedingTimeline.LastDecodeTime);
                    long clock = writer.Timestamp(precedingTimeline.LastClockDecodeTime);
                    if (clock < PcrLead || previous < clock)
                    {
                        throw new ArgumentOutOfRangeException(nameof(precedingTimeline));
                    }
                    writer.lastDecodeTime = previous;
                    long gap = previous - clock;
                    writer.lastPcr = clock - PcrLead + (gap > 0 ? (gap - 1) / PcrInterval * PcrInterval : 0);
                }
                success = true;
                return writer;
            }
            finally { if (!success) { writer.scratch.Dispose(); } }
        }

        /// <summary>Writes the final access unit for this track and completes its continuity-counter cycle.
        /// Only valid for a writer created with CreateIndexedSegment.</summary>
        public ValueTask WriteFinalPacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            if (!alignedSegment) { throw new InvalidOperationException("Final track packets require an indexed segment writer."); }
            return WritePacketCoreAsync(packet, true, cancellationToken);
        }

        private static int AlignedPacketCount(int payloadBytes, int initialAdaptationBytes, int previousCounter)
        {
            int minimum = checked((int)(((long)payloadBytes + initialAdaptationBytes + MpegTsPacket.PayloadSize - 1) / MpegTsPacket.PayloadSize));
            int count = checked(minimum + ((15 - previousCounter - minimum) & 15));
            if (count > payloadBytes)
            {
                throw new InvalidDataException("The final PES is too short to complete its continuity-counter cycle.");
            }
            return count;
        }
    }
}
