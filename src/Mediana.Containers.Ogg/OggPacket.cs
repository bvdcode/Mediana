// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Ogg
{
    /// <summary>An owned encoded packet. Only the last completed packet on a page carries that page's granule.
    /// Link indices distinguish chained streams, including links that reuse a serial number.</summary>
    public class OggPacket : IDisposable
    {
        private readonly MediaBuffer data;

        internal OggPacket(MediaBuffer data, uint serial, int linkIndex, long packetIndex,
            long? granule, bool beginning, bool end, long pageOffset, long startPage, int startLace, bool endsPage)
        {
            this.data = data;
            Serial = serial;
            LinkIndex = linkIndex;
            PacketIndex = packetIndex;
            GranulePosition = granule;
            IsBeginning = beginning;
            IsEnd = end;
            CompletionPageOffset = pageOffset;
            StartPageOffset = startPage;
            StartLace = startLace;
            EndsPage = endsPage;
        }

        public uint Serial { get; }
        public int LinkIndex { get; }
        public long PacketIndex { get; }
        public long? GranulePosition { get; }
        public bool IsBeginning { get; }
        public bool IsEnd { get; }
        public long CompletionPageOffset { get; }
        internal long StartPageOffset { get; }
        internal int StartLace { get; }
        internal bool EndsPage { get; }
        public ReadOnlyMemory<byte> Data => data.Memory;
        public MediaBuffer RetainData() => data.Retain();
        public void Dispose() => data.Dispose();
    }
}
