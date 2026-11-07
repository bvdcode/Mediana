// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;

namespace Mediana.Containers.Ogg
{
    internal abstract class OggTrack : IDisposable
    {
        protected OggTrack(int streamIndex, OggPacket packet, MediaBufferAllocator allocator)
        {
            if (!packet.IsBeginning || packet.PacketIndex != 0 || !packet.EndsPage ||
                packet.StartPageOffset != packet.CompletionPageOffset || packet.GranulePosition != 0 || packet.IsEnd)
            { throw new InvalidDataException("An Ogg identification packet must occupy its beginning page with granule zero."); }
            StreamIndex = streamIndex; Serial = packet.Serial; LinkIndex = packet.LinkIndex; Index = new(allocator);
        }
        internal static OggTrack Create(int streamIndex, OggPacket packet, MediaBufferAllocator allocator)
        {
            if (packet.Data.Length >= 7 && packet.Data.Span[0] == 0x80 && packet.Data.Span.Slice(1, 6).SequenceEqual("theora"u8))
            { return new OggTheoraTrack(streamIndex, packet, allocator); }
            return new OggAudioTrack(streamIndex, packet, allocator);
        }
        public int StreamIndex { get; }
        public uint Serial { get; }
        public int LinkIndex { get; }
        public OggPacketIndex Index { get; }
        public long NextPacket { get; set; }
        public virtual int SeekPreroll => 0;
        public virtual int MinimumSeekPackets => 0;
        public abstract TimeBase Clock { get; }
        public abstract void Add(OggPacket packet, CancellationToken token);
        public abstract (EncodedStreamInfo Stream, ContainerStreamInfo Info, MediaMetadata Metadata) Complete();
        internal long RandomAccessPacket(long output)
        {
            long key = Index[output].RandomAccessPacket;
            return key < 0 ? output : key;
        }
        public void Dispose() => Index.Dispose();
    }
}
