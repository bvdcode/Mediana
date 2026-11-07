// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class Mp4EncodedFragmentTests
    {
        [Fact]
        public async Task FinitePresentationEditPreservesExactTailAndZeroFragmentHeaderDurations()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            EncodedStreamInfo stream = new(7, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 44100), AacConfiguration.Create(44100, 1).ToAudioSpecificConfig());
            MediaTimestamp end = new(65777, new(1, 44100));
            Mp4AudioTrackDefinition definition = new(stream, 13, 1024, end);
            using MemoryStream output = new();
            await Mp4EncodedFragmentWriter.WriteInitializationAsync([definition], output, allocator);
            ReadOnlyMemory<byte> bytes = output.ToArray();
            ReadOnlyMemory<byte> movie = Mp4TestBoxes.Get(bytes, "moov", "mvhd");
            Assert.Equal(0u, Mp4TestBoxes.UInt32(movie, 16));
            ReadOnlyMemory<byte> track = Mp4TestBoxes.Get(bytes, "moov", "trak");
            Assert.Equal(0u, Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(track, "tkhd"), 20));
            ReadOnlyMemory<byte> edit = Mp4TestBoxes.Get(track, "edts", "elst");
            Assert.Equal(1u, Mp4TestBoxes.UInt32(edit, 4));
            Assert.Equal(1024ul, Mp4TestBoxes.UInt64(edit, 16));
            Assert.Equal(end, new MediaTimestamp((long)Mp4TestBoxes.UInt64(edit, 8), new(1, (int)Mp4TestBoxes.UInt32(movie, 12))));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4AudioTrackDefinition(stream, 13, presentationEnd: new(0, new(1, 1))));
        }

        [Fact]
        public async Task AudioAndVideoInitializationPublishesExplicitOffsetAndAllTrackDescriptions()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Encoder encoder = H264Encoder.Create(H264EncoderTests.Format(16, 16), new(1, 1000), allocator);
            EncodedStreamInfo stream = new(7, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 48000), AacConfiguration.Create(48000, 2).ToAudioSpecificConfig());
            Mp4TrackDefinition[] tracks = [new Mp4VideoTrackDefinition(encoder.Stream, H264EncoderTests.Format(16, 16), 3), new Mp4AudioTrackDefinition(stream, 11, 1024)];
            using MemoryStream output = new();
            await Mp4EncodedFragmentWriter.WriteInitializationAsync(tracks, output, allocator);
            byte[] bytes = output.ToArray();
            Assert.Equal(2, Count(bytes, "trak"u8));
            Assert.Equal(2, Count(bytes, "trex"u8));
            int offset = bytes.AsSpan().IndexOf("elst"u8) + 4;
            Assert.Equal(1, bytes[offset]);
            Assert.Equal(0ul, BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset + 8)));
            Assert.Equal(1024ul, BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset + 16)));
            int audio = bytes.AsSpan().IndexOf("mp4a"u8) + 4;
            Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(audio + 16)));
            Assert.Equal(48000u << 16, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(audio + 24)));
            int esds = bytes.AsSpan().IndexOf("esds"u8) + 4;
            Assert.Contains((byte)0x40, bytes[(esds + 4)..]);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.True(output.CanWrite);
        }

        [Fact]
        public async Task NegativeAudioPrimingMapsToNonnegativeStoredTimeWithoutChangingPacketLeases()
        {
            MediaBufferAllocator allocator = new(1024);
            EncodedStreamInfo stream = new(7, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 48000), AacConfiguration.Create(48000, 1).ToAudioSpecificConfig());
            Mp4AudioTrackDefinition track = new(stream, 13, 1024);
            using WritableMediaBuffer storage = allocator.Allocate(3);
            storage.Span.Fill(42);
            using MediaBuffer data = storage.Publish();
            using MediaPacket packet = new(7, data, new(-1024, new(1, 48000)), new(-1024, new(1, 48000)), new(1024, new(1, 48000)));
            using MemoryStream output = new();
            await using (Mp4EncodedFragmentPacketWriter writer = new([track], 23, output, allocator))
            {
                await writer.WritePacketAsync(packet);
                Assert.Equal(0, output.Length);
                await writer.CompleteAsync();
                Assert.Equal(output.Length, writer.BytesWritten);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.CompleteAsync());
            }
            ReadOnlyMemory<byte> bytes = output.ToArray();
            Assert.Equal(0ul, Mp4TestBoxes.UInt64(Mp4TestBoxes.Get(bytes, "moof", "traf", "tfdt"), 4));
            Assert.Equal(new byte[] { 42, 42, 42 }, Mp4TestBoxes.Get(bytes, "mdat").ToArray());
            Assert.Equal(-1024, packet.DecodeTime!.Value.Value);
            Assert.Equal(3, allocator.AllocatedBytes);
            Assert.True(output.CanWrite);
        }

        [Fact]
        public async Task PublicAudiovisualProcessingWritesOneSharedFragmentWithIndependentCodecDeclarations()
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-aac.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            long baseline = allocator.AllocatedBytes;
            MediaEngine engine = new MediaEngineBuilder().AddVideoDecoder(new H264DecoderFactory()).AddAudioDecoder(new AacDecoderFactory()).Build();
            using MemoryStream output = new();
            await using (MediaProcessingSession session = await engine.OpenProcessingAsync(reader,
                [new VideoEncodingRequest(0, 0, new H264EncoderFactory()), new AudioEncodingRequest(1, 7, new AacEncoderFactory())], allocator))
            {
                Mp4TrackDefinition[] tracks = [new Mp4VideoTrackDefinition(session.Streams[0], new(64, 48, Mediana.Core.Video.VideoPixelFormat.Yuv420P8), 3), new Mp4AudioTrackDefinition(session.Streams[1], 11, 1024)];
                await Mp4EncodedFragmentWriter.WriteInitializationAsync(tracks, output, allocator);
                await using Mp4EncodedFragmentPacketWriter writer = new(tracks, 1, output, allocator);
                MediaProcessingResult result = await session.WriteAsync(writer);
                Assert.Equal(76800, result.Tracks[1].SamplesPerChannel);
                Assert.Equal(2, Count(output.ToArray(), "traf"u8));
            }
            Assert.Equal(baseline, allocator.AllocatedBytes);
        }

        private static int Count(ReadOnlySpan<byte> input, ReadOnlySpan<byte> token)
        {
            int count = 0;
            int index;
            while ((index = input.IndexOf(token)) >= 0) { count++; input = input[(index + token.Length)..]; }
            return count;
        }
    }
}
