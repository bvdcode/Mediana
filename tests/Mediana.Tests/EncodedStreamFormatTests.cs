// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Codecs.Flac;
using Mediana.Codecs.H264;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Pcm;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;

namespace Mediana.Tests
{
    public class EncodedStreamFormatTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void AudioDecodersRejectIncompatibleRepresentationsBeforeParsingBytes(int index)
        {
            IAudioDecoderFactory factory = index switch
            {
                0 => new AacDecoderFactory(),
                1 => new Mp3DecoderFactory(),
                2 => new FlacDecoderFactory(),
                3 => new PcmDecoderFactory(),
                4 => new AiffPcmDecoderFactory(),
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
            EncodedStreamFormat format = new("unrecognized.config", "unrecognized.payload", EncodedPacketBoundary.ArbitraryBytes);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, factory.CodecId, format, new(1, 48000));
            MediaBufferAllocator allocator = new(1024);
            Assert.Contains("Expected", factory.GetUnsupportedReason(stream), StringComparison.Ordinal);
            Assert.Throws<NotSupportedException>(() => factory.Create(stream, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("avc.decoder-configuration-record", "avc.annex-b", EncodedPacketBoundary.AccessUnit)]
        [InlineData("avc.decoder-configuration-record", "avc.length-prefixed", EncodedPacketBoundary.ArbitraryBytes)]
        [InlineData("avc.parameter-sets", "avc.length-prefixed", EncodedPacketBoundary.AccessUnit)]
        public void VideoAndTransportRequireAllRepresentationComponents(string syntax, string framing, EncodedPacketBoundary boundary)
        {
            EncodedStreamInfo stream = new(0, MediaKind.Video, "h264", new(syntax, framing, boundary), new(1, 90000));
            MediaBufferAllocator allocator = new(1024);
            H264DecoderFactory decoder = new();
            Assert.NotNull(decoder.GetUnsupportedReason(stream));
            Assert.Throws<NotSupportedException>(() => decoder.Create(stream, allocator));
            Assert.Throws<NotSupportedException>(() => new H264RandomAccessInspector(stream));
            Assert.Throws<NotSupportedException>(() => new H264StreamMetadataReader().Read(stream));
            using MemoryStream output = new();
            Assert.Throws<NotSupportedException>(() => MpegTsWriter.Create(output, [stream], allocator));
            Assert.Equal(0, output.Length);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RepresentationHasValueEqualityAndRequiresExplicitValidIdentifiers()
        {
            Assert.Equal(StreamFormats.Avc, new EncodedStreamFormat("avc.decoder-configuration-record", "avc.length-prefixed", EncodedPacketBoundary.AccessUnit));
            Assert.Throws<ArgumentException>(() => new EncodedStreamFormat("", "bytes", EncodedPacketBoundary.ArbitraryBytes));
            Assert.Throws<ArgumentException>(() => new EncodedStreamFormat("none", " ", EncodedPacketBoundary.ArbitraryBytes));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EncodedStreamFormat("none", "bytes", (EncodedPacketBoundary)99));
            Assert.Throws<ArgumentNullException>(() => new EncodedStreamInfo(0, MediaKind.Video, "custom", null!, new TimeBase(1, 1)));
        }
    }
}
