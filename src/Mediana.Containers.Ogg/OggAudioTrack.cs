// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;

namespace Mediana.Containers.Ogg
{
    internal class OggAudioTrack(int streamIndex, OggPacket packet, MediaBufferAllocator allocator) : OggTrack(streamIndex, packet, allocator)
    {
        private readonly OggAudioHeaders headers = OggAudioHeaders.Create(packet.Data.Span);
        private int receivedHeaders = 1;
        private long nextTime;
        private long previousPageEnd;
        private long pageFirstPacket;
        private long origin;
        private bool anchored;
        private bool ended;

        public long PayloadBytes { get; private set; }
        public long CodedSamples { get; private set; }
        public long EndTime { get; private set; }
        public override int SeekPreroll => headers.SeekPreroll;
        public override int MinimumSeekPackets => headers.MinimumSeekPackets;
        public override TimeBase Clock => new(1, headers.SampleRate);

        public override void Add(OggPacket packet, CancellationToken token)
        {
            if (ended) { throw new InvalidDataException("An Ogg audio packet follows the stream end."); }
            if (receivedHeaders < headers.HeaderCount)
            {
                headers.ReadHeader(packet, token);
                receivedHeaders++;
                if (packet.IsEnd || packet.GranulePosition is long granule && granule != 0 ||
                    (receivedHeaders == headers.HeaderCount && !packet.EndsPage))
                {
                    throw new InvalidDataException("Ogg audio headers must end on a page boundary with granule zero before audio.");
                }
                return;
            }
            int samples = headers.ReadSamples(packet.Data.Span);
            bool first = Index.Count == 0;
            if (first) { nextTime = -headers.InitialLead(samples); }
            int presentation = first && headers.FirstPacketIsPreroll ? 0 : samples;
            Index.Add(new(packet.StartPageOffset, nextTime, samples, presentation, packet.Data.Length, packet.StartLace));
            nextTime = checked(nextTime + samples);
            CodedSamples = checked(CodedSamples + samples);
            PayloadBytes = checked(PayloadBytes + packet.Data.Length);
            if (packet.GranulePosition is long position) { AnchorPage(position, packet.IsEnd, token); }
            if (packet.IsEnd)
            {
                if (packet.GranulePosition is null) { throw new InvalidDataException("The final Ogg audio packet has no granule position."); }
                ended = true;
            }
        }

        private void AnchorPage(long granule, bool final, CancellationToken token)
        {
            long end = checked(granule - headers.PreSkip);
            if (!anchored && !final)
            {
                origin = checked(end - nextTime);
                if (!headers.AllowNegativeOrigin && origin < 0)
                {
                    throw new InvalidDataException("The first Ogg Opus audio page has a granule smaller than its coded duration.");
                }
                for (long index = 0; index < Index.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    OggIndexedPacket entry = Index[index];
                    Index[index] = entry with { Timestamp = checked(entry.Timestamp + origin) };
                }
                nextTime = checked(nextTime + origin);
            }
            else if (!final && end != nextTime)
            {
                throw new InvalidDataException("An Ogg audio page granule does not match its coded packet durations.");
            }
            if (final)
            {
                if (end > nextTime || (anchored && end < previousPageEnd) || end < Math.Max(0, origin))
                {
                    throw new InvalidDataException("The final Ogg granule lies outside its audio page.");
                }
                for (long index = pageFirstPacket; index < Index.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    OggIndexedPacket entry = Index[index];
                    long available = end - entry.Timestamp;
                    int duration = (int)Math.Clamp(available, 0, entry.PresentationSamples);
                    Index[index] = entry with { PresentationSamples = duration };
                }
                EndTime = end;
            }
            anchored = true;
            previousPageEnd = end;
            pageFirstPacket = Index.Count;
        }

        public override (EncodedStreamInfo Stream, ContainerStreamInfo Info, MediaMetadata Metadata) Complete()
        {
            if (receivedHeaders != headers.HeaderCount || !ended || Index.Count == 0)
            {
                throw new InvalidDataException("An Ogg audio stream is missing headers, audio packets or its final granule.");
            }
            MediaDuration duration = new(checked(EndTime - Math.Max(0, origin)), Clock);
            EncodedStreamInfo stream = new(StreamIndex, MediaKind.Audio, headers.CodecId, headers.Format, Clock,
                headers.Configuration.Span, codedDuration: new(CodedSamples, Clock));
            ContainerStreamInfo info = new(StreamIndex, duration, headers.Comments!.Tags,
                new(Index.Count, PayloadBytes, new(CodedSamples, Clock)));
            List<MediaProperty> properties = [new("ogg", "Serial", Serial), new("ogg", "LinkIndex", LinkIndex),
                new("ogg", "StreamIndex", StreamIndex), new(headers.CodecId, "SampleRate", headers.SampleRate),
                new(headers.CodecId, "ChannelCount", headers.ChannelCount), new(headers.CodecId, "Vendor", headers.Comments.Vendor),
                new("ogg", "PacketCount", Index.Count), new("ogg", "EncodedPayloadBytes", PayloadBytes),
                new("ogg", "PresentationStart", Math.Max(0, origin)), new("ogg", "PresentationEnd", EndTime)];
            if (headers.PreSkip != 0) { properties.Add(new(headers.CodecId, "PreSkipSamples", headers.PreSkip)); }
            headers.AddMetadata(properties);
            return (stream, info, new("stream", properties));
        }

    }
}
