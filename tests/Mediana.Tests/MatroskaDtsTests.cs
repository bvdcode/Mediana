// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Dts;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;
using Mediana.IO;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaReaderTestData;

namespace Mediana.Tests
{
    public class MatroskaDtsTests
    {
        [Fact]
        public Task WaveExportPreservesEverySpeakerAndSample() =>
            DtsWaveExportTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Dts"));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LacedCoreFramesUseExactSamplesAndOnlyTrimTheFinalFrame(bool clipped)
        {
            byte[] frame = await FirstFrameAsync();
            DtsFrameHeader header = DtsFrameHeader.Read(frame);
            Assert.Equal(512, header.SamplesPerFrame);
            byte[] block = clipped ? Field(0xA0, [.. Block(0xA1, flags: 4, payload: [1, .. frame, .. frame]), .. Unsigned(0x9B, 20)]) :
                Block(flags: 0x84, payload: [1, .. frame, .. frame]);
            byte[] data = Create(block);
            int firstPayload = data.AsSpan().IndexOf(frame);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(data)
            {
                MaximumRead = 3,
                ExcludedReadRanges = [(firstPayload + 256, firstPayload + frame.Length),
                    (firstPayload + frame.Length + 256, firstPayload + 2 * frame.Length)]
            };
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(reader.GetStreamMetadata(0));
                Assert.Equal(48000, metadata.SampleRate);
                Assert.Equal(2, metadata.ChannelCount);
                int calls = source.ReadCalls;
                Assert.Equal(new MediaDuration(512, new(1, 48000)), reader.GetPacketTiming(0, 0).Duration);
                MediaDuration last = reader.GetPacketTiming(0, 1).Duration!.Value;
                Assert.Equal(0, new MediaTimestamp(last.Value, last.TimeBase).CompareTo(new(clipped ? 448 : 512, new(1, 48000))));
                Assert.Equal(new MediaTimestamp(10666667, new(1, 1000000000)), reader.GetPacketTiming(0, 1).PresentationTime);
                Assert.Equal(calls, source.ReadCalls);
                source.ExcludedReadRanges = null;
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new DtsDecoderFactory().Create(reader.Streams[0], allocator);
                for (int index = 0; index < 2; index++)
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    decoder.TrySend(packet);
                    using AudioFrame pcm = decoder.Receive().Value;
                    Assert.Equal(clipped && index == 1 ? 448 : 512, pcm.SamplesPerChannel);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ProbeReadsCoreMetadataWithoutAnAudioDecoder()
        {
            byte[] frame = await FirstFrameAsync();
            byte[] data = Create(Block(payload: frame));
            int payload = data.AsSpan().IndexOf(frame);
            await using EbmlElementTestSource source = new(data)
            { ExcludedReadRange = (payload + 256, payload + frame.Length) };
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddStreamMetadataReader(new DtsStreamMetadataReader()).Build();
            MediaInfo info = await engine.ProbeAsync(source);
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]);
            Assert.Equal(48000, metadata.SampleRate);
            Assert.Equal(new[] { "front-left", "front-right" }, metadata.ChannelOrder);
            Assert.Empty(engine.AudioDecoderImplementations);
        }

        [Fact]
        public async Task ContradictoryTrackMetadataAndTruncatedCoreAreRejectedWithoutLeaking()
        {
            byte[] frame = await FirstFrameAsync();
            foreach (byte[] data in new[] { Create(Block(payload: frame), 44100), Create(Block(payload: frame), channels: 1),
                Create(Block(payload: frame[..19])), Create(Block(payload: frame[..^1])) })
            {
                MediaBufferAllocator allocator = new(1024 * 1024);
                await using EbmlElementTestSource source = new(data);
                await Assert.ThrowsAsync<InvalidDataException>(async () => await MatroskaReader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        private static byte[] Create(byte[] block, int rate = 48000, int channels = 2)
        {
            byte[] audio = Field(0xE1, [.. Float(0xB5, rate), .. Unsigned(0x9F, (ulong)channels)]);
            byte[] track = Field(0x1654AE6B, Field(0xAE, [.. Unsigned(0xD7, 1), .. Unsigned(0x73C5, 2), .. Unsigned(0x83, 2),
                .. Field(0x86, "A_DTS"u8), .. Unsigned(0x23E383, 32000000), .. audio]));
            return Document(Field(0x1549A966, InfoFields), track, Cluster(Unsigned(0xE7, 0), block));
        }

        private static async Task<byte[]> FirstFrameAsync()
        {
            await using RandomAccessStreamByteSource source = new(System.IO.File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "Dts", "stereo.mkv")));
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, new(1024 * 1024));
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 0);
            return packet.Data.ToArray();
        }
    }
}
