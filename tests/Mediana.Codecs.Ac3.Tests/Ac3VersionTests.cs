// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Ac3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3VersionTests
    {
        [Theory]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        [InlineData(15)]
        [InlineData(16)]
        public async Task BackwardCompatibleEnhancedVersionsPreserveTheirSyntaxAndPcm(int version)
        {
            byte[] original = Ac3SyntheticFrame.Create(true, false), data = Version(original, version);
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            Assert.Equal(version, header.BitstreamId); Assert.True(header.IsEnhanced);
            Assert.True(new Ac3ReaderFactory().CanRead(data));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Ac3ReaderTestSource source = new(data);
            await using (Ac3Reader reader = await Ac3Reader.OpenAsync(source, allocator))
            { Assert.Equal(version, reader.FirstFrame.BitstreamId); }
            byte[] expected = Decode(original, 16, false, allocator);
            Assert.Equal(expected, Decode(data, version, false, allocator));
            Assert.Equal(expected, Decode(data, version, true, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(17)]
        [InlineData(31)]
        public void IncompatibleVersionsRemainExplicitlyUnsupported(int version)
        {
            byte[] data = Version(Ac3SyntheticFrame.Create(true, false), version);
            Assert.Throws<NotSupportedException>(() => Ac3FrameHeader.Read(data));
        }

        private static byte[] Decode(byte[] data, int version, bool configured, MediaBufferAllocator allocator)
        {
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "eac3", configured ? StreamFormats.Eac3Mp4 : StreamFormats.Eac3,
                new(1, 48000), configured ? new byte[] { 0, 0, (byte)(version << 1), 2, 0 } : []);
            Eac3DecoderFactory factory = new(false);
            Assert.Null(factory.GetUnsupportedReason(stream));
            using IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(stream, allocator);
            using MediaPacket packet = Ac3DecoderTests.Packet(data, allocator);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet)); decoder.CompleteInput();
            using AudioFrame frame = decoder.Receive().Value;
            Assert.Equal(1536, frame.SamplesPerChannel);
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            return frame.GetPlane(0).ToArray();
        }

        private static byte[] Version(byte[] data, int version)
        {
            for (int bit = 0; bit < 5; bit++)
            { data = Ac3TestBitWriter.WithBit(data, 40 + bit, (version & 1 << (4 - bit)) != 0); }
            return data;
        }
    }
}
