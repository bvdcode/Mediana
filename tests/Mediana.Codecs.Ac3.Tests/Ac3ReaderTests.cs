// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Ac3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3ReaderTests
    {
        [Fact]
        public async Task VariableBlockIndexReadsOnlyHeadersAndKeepsExactSamplesAndIndependentCursors()
        {
            byte[][] frames = [Ac3SyntheticFrame.Create(true, false, 1), Ac3SyntheticFrame.Create(true, false, 2),
                Ac3SyntheticFrame.Create(true, false, 3), Ac3SyntheticFrame.Create(true, false)];
            byte[] data = frames.SelectMany(value => value).ToArray();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Ac3ReaderTestSource source = new(data) { MaximumRead = 2, HeadersOnly = true };
            await using (Ac3Reader reader = await Ac3Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(32, source.ReadBytes); Assert.Equal(4, reader.GetPacketCount(0));
                Assert.Equal(new MediaDuration(3072, new(1, 48000)), reader.Streams[0].CodedDuration);
                Assert.Equal(0, allocator.AllocatedBytes);
                long before = source.ReadCalls;
                int[] first = [0, 256, 768, 1536], counts = [256, 512, 768, 1536];
                for (int index = 3; index >= 0; index--)
                {
                    ContainerPacketTiming timing = reader.GetPacketTiming(0, index);
                    Assert.Equal(new MediaTimestamp(first[index], new(1, 48000)), timing.PresentationTime);
                    Assert.Equal(new MediaDuration(counts[index], new(1, 48000)), timing.Duration);
                }
                Assert.Equal(2, reader.FindSeekPoint(0, new(1000, new(1, 48000)), 4)!.Value.PacketIndex);
                Assert.Equal(1, reader.GetPrerollStart(0, 2, 1).PacketIndex);
                Assert.Equal(before, source.ReadCalls);
                source.HeadersOnly = false;
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new Eac3DecoderFactory(false).Create(reader.Streams[0], allocator);
                for (int index = 3; index >= 0; index--)
                {
                    using MediaPacket random = await reader.ReadPacketAtAsync(0, index);
                    Assert.Equal(frames[index], random.Data.ToArray());
                }
                for (int index = 0; index < 4; index++)
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    decoder.TrySend(packet); Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                }
                decoder.CompleteInput();
                for (int index = 0; index < 4; index++)
                {
                    using AudioFrame frame = decoder.Receive().Value;
                    Assert.Equal(counts[index], frame.SamplesPerChannel); Assert.Equal(new MediaTimestamp(first[index], new(1, 48000)), frame.Timestamp);
                }
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes); Assert.False(source.Disposed);
        }

        [Theory]
        [InlineData("mono.ac3")]
        [InlineData("stereo-44100.ac3")]
        [InlineData("stereo.eac3")]
        public async Task SignatureSelectedReaderDecodesEverySample(string name)
        {
            byte[] bytes = await System.IO.File.ReadAllBytesAsync(Ac3DecoderTests.Data(name));
            Ac3ReaderFactory factory = new(); Assert.True(factory.CanRead(bytes));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Ac3ReaderTestSource source = new(bytes);
            await using (IContainerReader reader = await factory.OpenAsync(source, allocator))
            {
                Ac3DecoderFactory codec = name.EndsWith(".eac3", StringComparison.Ordinal) ? new Eac3DecoderFactory() : new Ac3DecoderFactory();
                using IMediaCodec<MediaPacket, AudioFrame> decoder = codec.Create(reader.Streams[0], allocator);
                int total = 0;
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        decoder.TrySend(packet);
                        while (decoder.Receive() is { Status: CodecOutputStatus.Available } available)
                        { using AudioFrame frame = available.Value; total += frame.SamplesPerChannel; }
                    }
                }
                decoder.CompleteInput();
                while (decoder.Receive() is { Status: CodecOutputStatus.Available } available)
                { using AudioFrame frame = available.Value; total += frame.SamplesPerChannel; }
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                Assert.Equal(6144, total);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FailedAndCanceledReadsPreserveCursorAndRetainedPacketsSurviveDisposal()
        {
            byte[] bytes = Ac3DecoderTests.ReadFirstFrame("mono.ac3");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Ac3ReaderTestSource source = new(bytes);
            Ac3Reader reader = await Ac3Reader.OpenAsync(source, allocator);
            source.FailNext = true;
            await Assert.ThrowsAsync<IOException>(async () => await reader.ReadPacketAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
            using CancellationTokenSource cancellation = new(); await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadPacketAsync(cancellation.Token));
            using MediaPacket retained = (await reader.ReadPacketAsync())!;
            await reader.DisposeAsync(); Assert.Equal(bytes, retained.Data.ToArray()); Assert.False(source.Disposed);
            retained.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await reader.ReadPacketAsync());
        }

        [Fact]
        public async Task IndexCancellationAndTruncatedFramesReleaseTheirPrefixStorage()
        {
            byte[] frame = Ac3DecoderTests.ReadFirstFrame("mono.ac3");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Ac3ReaderTestSource gated = new(frame) { Gate = new TaskCompletionSource().Task };
            using CancellationTokenSource cancellation = new();
            Task<Ac3Reader> pending = Ac3Reader.OpenAsync(gated, allocator, cancellation.Token).AsTask();
            Assert.False(pending.IsCompleted); await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Equal(0, allocator.AllocatedBytes);
            foreach (byte[] invalid in new[] { frame[..7], frame[..^1], new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 } })
            {
                await using Ac3ReaderTestSource source = new(invalid);
                await Assert.ThrowsAsync<InvalidDataException>(async () => await Ac3Reader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }
    }
}
