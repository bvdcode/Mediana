// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using System.Runtime.CompilerServices;
using Mediana.Codecs.Ac3;
using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaReaderTestData;

namespace Mediana.Tests
{
    public class MatroskaAc3Tests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PacketPrefixesPreserveLaceBoundariesAndSequentialPosition(bool laced)
        {
            byte[] frame = First("stereo-44100.ac3");
            byte[] payload = laced ? [1, .. frame, .. frame] : frame;
            byte flags = laced ? (byte)0x84 : (byte)0x80;
            byte[] data = File(Ac3FrameHeader.Read(frame), Cluster(Unsigned(0xE7, 0), Block(flags: flags, payload: payload)));
            int firstPayload = data.AsSpan().IndexOf(frame);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(data)
            { MaximumRead = 3, ExcludedReadRange = (firstPayload + 256, firstPayload + frame.Length) };
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                IIndexedPacketPrefixReader prefixes = reader;
                byte[] destination = Enumerable.Repeat((byte)0xa5, 12).ToArray();
                for (int index = laced ? 1 : 0; index >= 0; index--)
                {
                    Assert.Equal(8, await prefixes.ReadPacketPrefixAtAsync(0, index, destination.AsMemory(2, 8)));
                    Assert.Equal(frame[..8], destination[2..10]);
                    Assert.Equal(new byte[] { 0xa5, 0xa5 }, destination[..2]);
                    Assert.Equal(new byte[] { 0xa5, 0xa5 }, destination[10..]);
                }
                Assert.Equal(0, await prefixes.ReadPacketPrefixAtAsync(0, 0, Memory<byte>.Empty));
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await prefixes.ReadPacketPrefixAtAsync(0, reader.GetPacketCount(0), destination));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await prefixes.ReadPacketPrefixAtAsync(0, 0, destination, new CancellationToken(true)));
                source.ExcludedReadRange = null;
                using MediaPacket packet = (await reader.ReadPacketAsync())!;
                Assert.Equal(frame, packet.Data.ToArray());
                byte[] oversized = new byte[frame.Length + 17];
                Assert.Equal(frame.Length, await prefixes.ReadPacketPrefixAtAsync(0, 0, oversized));
                Assert.Equal(frame, oversized[..frame.Length]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task CommonProbeObtainsMatroskaAudioFactsFromTheRegisteredMetadataReader()
        {
            byte[] frame = First("stereo-44100.ac3");
            byte[] data = File(Ac3FrameHeader.Read(frame), Cluster(Unsigned(0xE7, 0), Block(payload: frame)));
            int firstPayload = data.AsSpan().IndexOf(frame);
            await using EbmlElementTestSource source = new(data)
            { ExcludedReadRange = (firstPayload + 256, firstPayload + frame.Length) };
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddStreamMetadataReader(new Ac3StreamMetadataReader()).Build();
            MediaInfo info = await engine.ProbeAsync(source);
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]);
            Assert.Equal(44100, metadata.SampleRate);
            Assert.Equal(2, metadata.ChannelCount);
            Assert.Empty(engine.AudioDecoderImplementations);
        }

        [Theory]
        [InlineData("mono.ac3")]
        [InlineData("stereo-44100.ac3")]
        [InlineData("stereo.eac3")]
        public async Task PublicReaderPreservesEverySyncframeAndDecodedSample(string name)
        {
            byte[] encoded = Read(name);
            Ac3FrameHeader first = Ac3FrameHeader.Read(encoded);
            List<byte[]> frames = [];
            for (int offset = 0; offset < encoded.Length;)
            {
                Ac3FrameHeader header = Ac3FrameHeader.Read(encoded.AsSpan(offset));
                frames.Add(encoded.AsSpan(offset, header.FrameBytes).ToArray()); offset += header.FrameBytes;
            }
            byte[][] blocks = frames.Select((bytes, index) => Block(timestamp: checked((short)Math.Round(
                index * first.SamplesPerFrame * 1000.0 / first.SampleRate)), payload: bytes)).ToArray();
            byte[] data = File(first, Cluster(Unsigned(0xE7, 0), blocks.SelectMany(value => value).ToArray()));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 3 };
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Assert.Equal(frames.Count, reader.GetPacketCount(0));
                EncodedStreamInfo stream = Assert.Single(reader.Streams);
                Assert.Equal(first.IsEnhanced ? "eac3" : "ac3", stream.CodecId);
                Assert.Equal("none", stream.Format.ConfigurationSyntax); Assert.True(stream.Configuration.IsEmpty);
                Ac3DecoderFactory factory = first.IsEnhanced ? new Eac3DecoderFactory(false) : new Ac3DecoderFactory(false);
                using IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(stream, allocator);
                using IMediaCodec<MediaPacket, AudioFrame> direct = factory.Create(stream, allocator);
                long readCalls = source.ReadCalls;
                for (int index = frames.Count - 1; index >= 0; index--)
                {
                    ContainerPacketTiming timing = reader.GetPacketTiming(0, index);
                    Assert.Equal(new MediaDuration(first.SamplesPerFrame, new(1, first.SampleRate)), timing.Duration);
                }
                Assert.Equal(readCalls, source.ReadCalls);
                int decoded = 0;
                for (int index = 0; index < frames.Count; index++)
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    Assert.Equal(frames[index], packet.Data.ToArray());
                    using MediaPacket reference = packet.RetainWithDuration(null);
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                    Assert.Equal(CodecInputStatus.Accepted, direct.TrySend(reference));
                    Drain();
                }
                Assert.Null(await reader.ReadPacketAsync());
                decoder.CompleteInput(); direct.CompleteInput(); Drain();
                Assert.Equal(frames.Count, decoded);
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);

                void Drain()
                {
                    while (true)
                    {
                        CodecOutput<AudioFrame> output = decoder.Receive();
                        CodecOutput<AudioFrame> reference = direct.Receive();
                        Assert.Equal(reference.Status, output.Status);
                        if (output.Status != CodecOutputStatus.Available) { return; }
                        using AudioFrame actual = output.Value;
                        using AudioFrame expected = reference.Value;
                        Assert.Equal(first.SamplesPerFrame, actual.SamplesPerChannel);
                        Assert.Equal(reader.GetPacketTiming(0, decoded++).PresentationTime, actual.Timestamp);
                        Assert.Equal(expected.GetPlane(0).ToArray(), actual.GetPlane(0).ToArray());
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes); Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task VariableEac3LaceUsesEveryFramesOwnSampleCountInsteadOfDefaultDuration(bool clipped)
        {
            byte[][] frames = [Read("enhanced-one.eac3"), Read("enhanced-two.eac3"), Read("enhanced-three.eac3")];
            byte[] payload = [2, .. frames[0], .. frames[1], .. frames[2]];
            byte[] block = clipped ? Field(0xA0, [.. Block(0xA1, flags: 4, payload: payload), .. Unsigned(0x9B, 20)]) :
                Block(flags: 0x84, payload: payload);
            Ac3FrameHeader header = Ac3FrameHeader.Read(frames[0]);
            byte[] data = File(header, Cluster(Unsigned(0xE7, 0), block), defaultDuration: 32000000);
            int firstPayload = data.AsSpan().IndexOf(frames[0]);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(data)
            { MaximumRead = 2, ExcludedReadRange = (firstPayload + 256, firstPayload + frames[0].Length) };
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                long readCalls = source.ReadCalls;
                MediaTimestamp[] pts = [new(0, new(1, 1000000000)), new(5333333, new(1, 1000000000)), new(16000000, new(1, 1000000000))];
                for (int index = 2; index >= 0; index--)
                {
                    ContainerPacketTiming timing = reader.GetPacketTiming(0, index);
                    Assert.Equal(pts[index], timing.PresentationTime);
                    int expectedSamples = clipped && index == 2 ? 192 : (index + 1) * 256;
                    MediaDuration duration = timing.Duration!.Value;
                    Assert.Equal(0, new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new(expectedSamples, new(1, 48000))));
                }
                Assert.Equal(readCalls, source.ReadCalls);
                source.ExcludedReadRange = null;
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new Eac3DecoderFactory(false).Create(reader.Streams[0], allocator);
                int samples = 0;
                for (int index = 0; index < 3; index++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, index);
                    decoder.TrySend(packet);
                    Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                }
                decoder.CompleteInput();
                for (int index = 0; index < 3; index++)
                {
                    using AudioFrame pcm = decoder.Receive().Value;
                    Assert.Equal(clipped && index == 2 ? 192 : (index + 1) * 256, pcm.SamplesPerChannel);
                    Assert.Equal(pts[index], pcm.Timestamp); samples += pcm.SamplesPerChannel;
                }
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                Assert.Equal(clipped ? 960 : 1536, samples);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ExplicitPresentationDurationRemainsDistinctFromCodedSampleCount()
        {
            byte[] frame = First("mono.ac3");
            byte[] group = Field(0xA0, [.. Block(0xA1, flags: 0, payload: frame), .. Unsigned(0x9B, 20)]);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(File(Ac3FrameHeader.Read(frame), Cluster(Unsigned(0xE7, 0), group)));
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Assert.Equal(new MediaDuration(20000000, new(1, 1000000000)), reader.GetPacketTiming(0, 0).Duration);
                using MediaPacket packet = await reader.ReadPacketAtAsync(0, 0);
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new Ac3DecoderFactory(false).Create(reader.Streams[0], allocator);
                decoder.TrySend(packet); using AudioFrame pcm = decoder.Receive().Value;
                Assert.Equal(960, pcm.SamplesPerChannel);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InvalidMappingFrameBoundaryAndTrackFormatFailBeforeDecoding()
        {
            byte[] frame = First("mono.ac3");
            Ac3FrameHeader header = Ac3FrameHeader.Read(frame);
            foreach (byte[] invalid in new[]
            {
                File(header, Cluster(Unsigned(0xE7, 0), Block(payload: frame)), codec: "A_EAC3"),
                File(header, Cluster(Unsigned(0xE7, 0), Block(payload: [.. frame, .. frame]))),
                File(header, Cluster(Unsigned(0xE7, 0), Block(payload: frame[..7]))),
                File(header, Cluster(Unsigned(0xE7, 0), Block(payload: frame)), sampleRate: 44100),
                File(header, Cluster(Unsigned(0xE7, 0), Block(payload: frame)), channels: 2)
            })
            {
                MediaBufferAllocator allocator = new(1024 * 1024);
                await using EbmlElementTestSource source = new(invalid);
                await Assert.ThrowsAsync<InvalidDataException>(async () => await MatroskaReader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes); Assert.False(source.IsDisposed);
            }
        }

        [Fact]
        public async Task PrefixCancellationAndTimingIndexBudgetFailureReleaseTheirBuffers()
        {
            byte[] frame = First("mono.ac3");
            byte[] data = File(Ac3FrameHeader.Read(frame), Cluster(Unsigned(0xE7, 0), Block(payload: frame)));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource gated = new(data)
            { ReadGate = new TaskCompletionSource().Task, ReadGateOffset = data.Length - frame.Length };
            using CancellationTokenSource cancellation = new();
            Task<MatroskaReader> pending = MatroskaReader.OpenAsync(gated, allocator, cancellationToken: cancellation.Token).AsTask();
            Assert.False(pending.IsCompleted); await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(0, allocator.AllocatedBytes);

            await using EbmlElementTestSource source = new(data);
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                int indexBytes = Unsafe.SizeOf<MatroskaIndexedBlock>() + Unsafe.SizeOf<MatroskaTrackBlock>() + sizeof(ulong);
                await Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await MatroskaReader.OpenAsync(source, allocator, new() { MaxIndexBytes = indexBytes }));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static byte[] Read(string name) => System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", name));
        private static byte[] First(string name)
        {
            byte[] bytes = Read(name);
            return bytes[..Ac3FrameHeader.Read(bytes).FrameBytes];
        }

        private static byte[] File(Ac3FrameHeader header, byte[] cluster, string? codec = null,
            int? sampleRate = null, int? channels = null, ulong? defaultDuration = null)
        {
            byte[] audio = Field(0xE1, [.. Float(0xB5, sampleRate ?? header.SampleRate), .. Unsigned(0x9F, (ulong)(channels ?? header.ChannelCount))]);
            byte[] duration = defaultDuration is ulong ns ? Unsigned(0x23E383, ns) : [];
            byte[] track = Field(0x1654AE6B, Field(0xAE, [.. Unsigned(0xD7, 1), .. Unsigned(0x73C5, 2), .. Unsigned(0x83, 2),
                .. Field(0x86, System.Text.Encoding.UTF8.GetBytes(codec ?? (header.IsEnhanced ? "A_EAC3" : "A_AC3"))),
                .. Field(0x63A2, [0xff, 0xff]), .. audio, .. duration]));
            return Document(Field(0x1549A966, InfoFields), track, cluster);
        }
    }
}
