// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana.Streaming.Hls
{
    internal class HlsCopiedAudioTrack : IHlsAudioTrack
    {
        private const int InitialPrerollPackets = 2;
        private readonly IIndexedPacketTimingReader reader;
        private readonly int sourceStreamIndex;

        public HlsCopiedAudioTrack(IIndexedPacketTimingReader reader, int sourceStreamIndex, int outputStreamIndex, uint trackId)
        {
            this.reader = reader;
            this.sourceStreamIndex = sourceStreamIndex;
            EncodedStreamInfo source = reader.Streams.SingleOrDefault(stream => stream.StreamIndex == sourceStreamIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(sourceStreamIndex));
            EncodedStreamInfo output = new(outputStreamIndex, source.Kind, source.CodecId, source.Format,
                source.TimeBase, source.Configuration.Span);
            long offset = 0;
            if (reader.GetPacketCount(sourceStreamIndex) > 0)
            {
                ContainerPacketTiming first = reader.GetPacketTiming(sourceStreamIndex, 0);
                MediaTimestamp pts = first.PresentationTime ?? throw new NotSupportedException("HLS audio requires indexed presentation times.");
                MediaTimestamp dts = first.DecodeTime ?? throw new NotSupportedException("HLS audio requires indexed decode times.");
                MediaTimestamp earliest = pts.CompareTo(dts) < 0 ? pts : dts;
                long ticks = earliest.Rescale(new(1, source.TimeBase.Denominator), TimestampRounding.TowardZero).Value;
                if (ticks < 0) { offset = checked(-ticks); }
            }
            Definition = new(output, trackId, offset);
        }

        public Mp4AudioTrackDefinition Definition { get; }

        public MediaTrackRequest CreateRequest(HlsVideoWindow window)
        {
            MediaTimestamp start = window.Start;
            if (window.Number == 0)
            {
                long count = reader.GetPacketCount(sourceStreamIndex);
                long lower = 0, upper = count;
                while (lower < upper)
                {
                    long middle = lower + (upper - lower) / 2;
                    MediaTimestamp time = reader.GetPacketTiming(sourceStreamIndex, middle).PresentationTime
                        ?? throw new NotSupportedException("HLS audio requires indexed presentation times.");
                    if (time.CompareTo(start) < 0) { lower = middle + 1; }
                    else { upper = middle; }
                }
                if (lower < count)
                {
                    MediaTimestamp next = reader.GetPacketTiming(sourceStreamIndex, lower).PresentationTime!.Value;
                    if (next.CompareTo(window.End) < 0)
                    {
                        MediaTimestamp preroll = reader.GetPacketTiming(sourceStreamIndex, Math.Max(0, lower - InitialPrerollPackets)).PresentationTime!.Value;
                        if (preroll.CompareTo(start) < 0) { start = preroll; }
                    }
                }
            }
            return new PacketCopyRequest(sourceStreamIndex, Definition.Stream.StreamIndex, PacketCopyAlignment.PresentationStarts, new(start, window.End));
        }
    }
}
