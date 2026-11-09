// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Containers;
using Mediana.Containers.Ac3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3DependentReaderTests
    {
        [Theory]
        [InlineData(false, 8, 2)]
        [InlineData(true, 16, 4)]
        public async Task IndexGroupsSynchronizedSubstreamsWithoutReadingPayloadsOrCountingTimeTwice(bool wide, int channels, int components)
        {
            byte[] data = Ac3DependentFrame.CreateSequence(wide);
            List<(long Start, long End)> ranges = [];
            int frameCount = 0;
            for (int offset = 0; offset < data.Length;)
            {
                Ac3FrameHeader frame = Ac3FrameHeader.Read(data.AsSpan(offset));
                ranges.Add((offset, offset + (frameCount < components ? 12 : 8))); frameCount++; offset += frame.FrameBytes;
            }
            byte[] reference = await File.ReadAllBytesAsync(Ac3DecoderTests.Data($"dependent-{channels}.pcm"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Ac3ReaderTestSource source = new(data) { MaximumRead = 3, HeadersOnly = true, AllowedReadRanges = ranges };
            await using (Ac3Reader reader = await Ac3Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(frameCount * 8 + components * 4, source.ReadBytes);
                Assert.Equal(channels, reader.ChannelOrder.Count);
                Assert.Equal(4, reader.GetPacketCount(0));
                Assert.Equal(new MediaDuration(3072, new(1, 48000)), reader.Streams[0].CodedDuration);
                Assert.Equal(0, allocator.AllocatedBytes);
                int calls = source.ReadCalls;
                AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(reader.GetStreamMetadata(0));
                Assert.Equal(channels, metadata.ChannelCount); Assert.Equal(reader.ChannelOrder, metadata.ChannelOrder);
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetStreamMetadata(1));
                for (int index = 3; index >= 0; index--)
                {
                    ContainerPacketTiming timing = reader.GetPacketTiming(0, index);
                    Assert.Equal(new MediaTimestamp(index * 768, new(1, 48000)), timing.PresentationTime);
                    Assert.Equal(new MediaDuration(768, new(1, 48000)), timing.Duration);
                }
                Assert.Equal(2, reader.FindSeekPoint(0, new(1700, new(1, 48000)), 4)!.Value.PacketIndex);
                Assert.Equal(calls, source.ReadCalls);
                source.HeadersOnly = false; source.AllowedReadRanges = null;
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new Eac3DecoderFactory(false).Create(reader.Streams[0], allocator);
                for (int index = 0; index < 4; index++)
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                    Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                }
                decoder.CompleteInput();
                for (int index = 0; index < 4; index++)
                {
                    using AudioFrame pcm = decoder.Receive().Value;
                    Assert.Equal(reader.ChannelOrder, pcm.Format.Channels);
                    Assert.Equal(new MediaTimestamp(index * 768, new(1, 48000)), pcm.Timestamp);
                    Assert.Equal(768, pcm.SamplesPerChannel);
                    ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(pcm.GetPlane(0).Span);
                    ReadOnlySpan<float> expected = MemoryMarshal.Cast<byte, float>(reference.AsSpan(index * 768 * channels * sizeof(float), pcm.GetPlane(0).Length));
                    for (int sample = 0; sample < actual.Length; sample++) { Assert.InRange(Math.Abs(actual[sample] - expected[sample]), 0, 1e-8f); }
                }
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes); Assert.False(source.Disposed);
        }
    }
}
