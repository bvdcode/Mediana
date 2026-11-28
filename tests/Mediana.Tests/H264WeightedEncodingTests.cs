// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264WeightedEncodingTests
    {
        [Theory]
        [InlineData(-20)]
        [InlineData(18)]
        public void ExposureChangesSwitchPictureParametersAndPreserveReconstruction(int offset)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(64, 48);
            TimeBase clock = new(1, 1000);
            using H264Encoder encoder = H264Encoder.Create(format, clock, allocator,
                new() { EntropyCoding = H264EntropyCoding.Cabac, Quantizer = 20 });
            H264DecoderOptions options = new();
            H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, options.HeaderLimits);
            Assert.False(configuration.Pictures[0].WeightedPrediction);
            Assert.True(configuration.Pictures[(int)H264EncoderConfiguration.WeightedPictureId].WeightedPrediction);

            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator);
            using VideoFrame first = Frame(format, clock, allocator, 0, 80, 95, 140);
            using MediaPacket firstPacket = Encode(encoder, first);
            Assert.Null(Header(firstPacket, configuration, options).Weights);
            using VideoFrame firstDecoded = Decode(decoder, firstPacket);
            byte luma = (byte)(firstDecoded.GetRow(0, 0)[0] + offset);
            byte cb = firstDecoded.GetRow(1, 0)[0];
            byte cr = firstDecoded.GetRow(2, 0)[0];

            using VideoFrame changed = Frame(format, clock, allocator, 40, luma, cb, cr);
            using MediaPacket changedPacket = Encode(encoder, changed);
            H264WeightTable weights = Assert.IsType<H264WeightTable>(Header(changedPacket, configuration, options).Weights);
            Assert.Equal((1, offset, 0), weights.Get(0, 0));
            using VideoFrame changedDecoded = Decode(decoder, changedPacket);
            Assert.Equal(changed.Data.ToArray(), changedDecoded.Data.ToArray());

            using VideoFrame steady = Frame(format, clock, allocator, 80, luma, cb, cr);
            using MediaPacket steadyPacket = Encode(encoder, steady);
            Assert.Null(Header(steadyPacket, configuration, options).Weights);
            using VideoFrame steadyDecoded = Decode(decoder, steadyPacket);
            Assert.Equal(steady.Data.ToArray(), steadyDecoded.Data.ToArray());
        }

        private static H264SliceHeader Header(MediaPacket packet, H264Configuration configuration, H264DecoderOptions options)
        {
            H264AccessUnit unit = H264AccessUnitReader.Read(packet.Data, configuration, options);
            ReadOnlySpan<byte> nal = Assert.Single(unit.Slices).Span;
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            return H264SliceHeaderReader.Read(ref bits, unit.Configuration, nal[0]);
        }

        private static MediaPacket Encode(H264Encoder encoder, VideoFrame frame)
        {
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(frame));
            return Assert.IsType<MediaPacket>(encoder.Receive().Value);
        }

        private static VideoFrame Decode(IMediaCodec<MediaPacket, VideoFrame> decoder, MediaPacket packet)
        {
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
            return Assert.IsType<VideoFrame>(decoder.Receive().Value);
        }

        private static VideoFrame Frame(VideoFormat format, TimeBase clock, MediaBufferAllocator allocator, long timestamp,
            byte luma, byte cb, byte cr)
        {
            PlaneLayout[] layout = format.CreateTightLayout();
            int end = (int)layout[^1].EndOffset;
            using WritableMediaBuffer storage = allocator.Allocate(end);
            storage.Span[..layout[1].Offset].Fill(luma);
            storage.Span[layout[1].Offset..layout[2].Offset].Fill(cb);
            storage.Span[layout[2].Offset..end].Fill(cr);
            using MediaBuffer data = storage.Publish();
            return new(format, data, layout, new(timestamp, clock), new(40, clock));
        }
    }
}
