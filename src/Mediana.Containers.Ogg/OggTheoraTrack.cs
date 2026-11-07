// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.Formats.Video;

namespace Mediana.Containers.Ogg
{
    internal class OggTheoraTrack : OggTrack
    {
        private readonly TheoraIdentification identification;
        private readonly byte[] first;
        private byte[] comment = [], configuration = [];
        private VorbisComments? comments;
        private int receivedHeaders = 1;
        private long lastKey = -1, origin, payloadBytes;
        private bool anchored, ended;
        public override TimeBase Clock { get; }

        internal OggTheoraTrack(int streamIndex, OggPacket packet, MediaBufferAllocator allocator) : base(streamIndex, packet, allocator)
        {
            identification = TheoraIdentification.Read(packet.Data.Span); first = packet.Data.ToArray();
            uint divisor = (uint)BigInteger.GreatestCommonDivisor(identification.FrameRateNumerator, identification.FrameRateDenominator);
            uint numerator = identification.FrameRateDenominator / divisor, denominator = identification.FrameRateNumerator / divisor;
            if (numerator > int.MaxValue || denominator > int.MaxValue) { throw new NotSupportedException("The Theora frame clock exceeds the supported exact time base."); }
            Clock = new((int)numerator, (int)denominator);
        }

        public override void Add(OggPacket packet, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ended) { throw new InvalidDataException("A Theora packet follows its Ogg stream end."); }
            if (receivedHeaders < 3)
            {
                if (packet.Data.Length < 7 || packet.Data.Span[0] != 0x80 + receivedHeaders || !packet.Data.Span.Slice(1, 6).SequenceEqual("theora"u8))
                { throw new InvalidDataException("The Theora headers are absent or out of order."); }
                if (packet.IsEnd || packet.GranulePosition is long granule && granule != 0 || receivedHeaders == 2 && !packet.EndsPage)
                { throw new InvalidDataException("Ogg Theora headers must end on a page boundary with granule zero before pictures."); }
                if (receivedHeaders == 1)
                { comments = VorbisComments.Read(packet.Data.Span[7..], out _, cancellationToken: token); comment = packet.Data.ToArray(); }
                else { configuration = XiphLacedHeaders.Write(first, comment, packet.Data.Span); comment = []; }
                receivedHeaders++; return;
            }
            if (!packet.Data.IsEmpty)
            {
                if ((packet.Data.Span[0] & 0x80) != 0) { throw new InvalidDataException("An Ogg Theora data packet has its header bit set."); }
                if ((packet.Data.Span[0] & 0x40) == 0) { lastKey = Index.Count; }
            }
            if (lastKey < 0) { throw new InvalidDataException("An Ogg Theora stream does not begin with a keyframe."); }
            Index.Add(new(packet.StartPageOffset, checked(Index.Count + origin), 1, 1, packet.Data.Length, packet.StartLace, lastKey));
            payloadBytes = checked(payloadBytes + packet.Data.Length);
            if (packet.GranulePosition is long position) { Anchor(position, token); }
            if (packet.IsEnd)
            {
                if (packet.GranulePosition is null) { throw new InvalidDataException("The final Ogg Theora picture has no granule position."); }
                ended = true;
            }
        }

        private void Anchor(long granule, CancellationToken token)
        {
            int shift = identification.GranuleShift;
            long keyCount = checked((granule >> shift) + (identification.Revision == 0 ? 1 : 0));
            long end = checked(keyCount + (granule & ((1L << shift) - 1)));
            if (!anchored)
            {
                origin = checked(end - Index.Count);
                if (origin < 0) { throw new InvalidDataException("The initial Theora granule precedes its coded picture count."); }
                for (long i = 0; i < Index.Count; i++)
                { token.ThrowIfCancellationRequested(); OggIndexedPacket entry = Index[i]; Index[i] = entry with { Timestamp = checked(entry.Timestamp + origin) }; }
                anchored = true;
            }
            if (end != checked(origin + Index.Count) || keyCount != checked(origin + lastKey + 1))
            { throw new InvalidDataException("An Ogg Theora granule does not match its picture and keyframe counts."); }
        }

        public override (EncodedStreamInfo Stream, ContainerStreamInfo Info, MediaMetadata Metadata) Complete()
        {
            if (receivedHeaders != 3 || !ended || Index.Count == 0 || !anchored)
            { throw new InvalidDataException("An Ogg Theora stream is missing headers, pictures or its final granule."); }
            MediaDuration duration = new(Index.Count, Clock);
            EncodedStreamInfo stream = new(StreamIndex, MediaKind.Video, TheoraIdentification.CodecId, StreamFormats.Theora, Clock, configuration, displayMatrix: VideoDisplayMatrix.Identity, codedDuration: duration);
            ContainerStreamInfo info = new(StreamIndex, duration, comments!.Tags, new(Index.Count, payloadBytes, duration),
                averageFrameRate: new(Clock.Denominator, Clock.Numerator));
            List<MediaProperty> properties = [new("ogg", "Serial", Serial), new("ogg", "LinkIndex", LinkIndex), new("ogg", "StreamIndex", StreamIndex),
                new("theora", "Width", identification.Width), new("theora", "Height", identification.Height),
                new("theora", "CodedWidth", identification.CodedWidth), new("theora", "CodedHeight", identification.CodedHeight),
                new("theora", "PictureX", identification.PictureX), new("theora", "PictureY", identification.PictureY),
                new("theora", "PixelFormat", identification.PixelFormat), new("theora", "ColorSpace", identification.ColorSpace),
                new("theora", "AspectRatioNumerator", identification.AspectRatioNumerator), new("theora", "AspectRatioDenominator", identification.AspectRatioDenominator),
                new("theora", "NominalBitRate", identification.NominalBitRate), new("theora", "Quality", identification.Quality),
                new("theora", "GranuleShift", identification.GranuleShift), new("theora", "Vendor", comments.Vendor),
                new("ogg", "PacketCount", Index.Count), new("ogg", "EncodedPayloadBytes", payloadBytes),
                new("ogg", "PresentationStart", origin), new("ogg", "PresentationEnd", checked(origin + Index.Count))];
            return (stream, info, new("stream", properties));
        }
    }
}
