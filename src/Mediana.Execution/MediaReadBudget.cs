// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Execution
{
    /// <summary>Cumulative operation limits shared by independent selected-stream cursors and decoders.
    /// Reads include seek inspection, discarded preroll and EOF checks. Null limits are unrestricted.</summary>
    public class MediaReadBudget
    {
        private readonly long? maximumPackets;
        private readonly long? maximumFrames;
        private long packets;
        private long frames;

        public MediaReadBudget(long? maximumPackets = null, long? maximumFrames = null)
        {
            if (maximumPackets is long packetLimit) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packetLimit); }
            if (maximumFrames is long frameLimit) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameLimit); }
            this.maximumPackets = maximumPackets;
            this.maximumFrames = maximumFrames;
        }

        public long PacketReads => Volatile.Read(ref packets);
        public long FrameReads => Volatile.Read(ref frames);
        public void CountPacketRead() => Count(ref packets, maximumPackets, "packet");
        public void CountFrameRead() => Count(ref frames, maximumFrames, "frame");

        private static void Count(ref long counter, long? maximum, string kind)
        {
            while (true)
            {
                long current = Volatile.Read(ref counter);
                if (maximum is long limit && current == limit)
                { throw new InvalidOperationException($"The configured operation limit of {limit} {kind} reads has been reached."); }
                if (Interlocked.CompareExchange(ref counter, checked(current + 1), current) == current) { return; }
            }
        }
    }
}
