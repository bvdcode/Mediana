// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class AacMultichannelTests
    {
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public async Task AllConfiguredSpeakersMatchIndependentCompletePcm(int configuration) =>
            await AacMultichannelTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacMultichannel"), configuration);

        [Fact]
        public async Task IndependentAndCommonWindowsPreserveEverySequenceAndShapeTransition() =>
            await AacMultichannelTestInput.VerifyTransitionsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacMultichannel"));

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void MissingDuplicateReorderedOrExtraElementsFaultWithoutOutput(int scenario)
        {
            (int, int)[] elements = scenario switch
            {
                0 => [(0, 0), (1, 0), (1, 1)],
                1 => [(0, 0), (1, 0), (1, 0), (3, 0)],
                2 => [(1, 0), (0, 0), (1, 1), (3, 0)],
                3 => [(0, 0), (1, 0), (1, 1), (0, 1)],
                4 => [(0, 0), (1, 0), (1, 1), (3, 0), (3, 1)],
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            MediaBufferAllocator allocator = new(512 * 1024);
            using MediaPacket packet = Packet(allocator, AacTestPackets.Elements(elements));
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(6), allocator);
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
            Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
        }

        [Fact]
        public void ChangedElementTagsFaultAndResetRestoresChannelBindings()
        {
            MediaBufferAllocator allocator = new(512 * 1024);
            using MediaPacket first = Packet(allocator, AacTestPackets.Elements((0, 0), (1, 0), (1, 1), (3, 0)));
            using MediaPacket changed = Packet(allocator, AacTestPackets.Elements((0, 1), (1, 0), (1, 1), (3, 0)));
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(6), allocator);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
            decoder.Receive().Value.Dispose();
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(changed));
            Assert.Equal(first.Data.Length + changed.Data.Length, allocator.AllocatedBytes);
            decoder.Reset();
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(changed));
            using AudioFrame frame = decoder.Receive().Value;
            Assert.Equal(6, frame.Format.Channels.Count);
            decoder.CompleteInput();
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            Assert.Equal(first.Data.Length + changed.Data.Length + frame.GetPlane(0).Length, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1024)]
        [InlineData(24000)]
        [InlineData(48000)]
        [InlineData(72000)]
        [InlineData(96000)]
        [InlineData(120000)]
        [InlineData(144000)]
        [InlineData(168000)]
        [InlineData(192000)]
        [InlineData(224000)]
        public void PartialMultichannelAllocationFailureReleasesAllDecoderStorage(int budget)
        {
            MediaBufferAllocator allocator = new(budget);
            using MediaPacket packet = Packet(allocator, AacTestPackets.Elements((0, 0), (1, 0), (1, 1), (1, 2), (3, 0)));
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(7), allocator);
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
            Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            decoder.Reset();
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
        }

        [Fact]
        public void CancellationReleasesEveryChannelAndPendingOutput()
        {
            MediaBufferAllocator allocator = new(512 * 1024);
            using MediaPacket packet = Packet(allocator, AacTestPackets.Elements((0, 0), (1, 0), (1, 1), (1, 2), (3, 0)));
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(7), allocator);
            decoder.TrySend(packet);
            using CancellationTokenSource source = new();
            source.Cancel();
            Assert.Throws<OperationCanceledException>(() => decoder.Receive(source.Token));
            Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
        }

        private static EncodedStreamInfo Stream(int configuration) => new(0, MediaKind.Audio, "aac", StreamFormats.Aac,
            new TimeBase(1, 48000), AacConfiguration.Create(48000, configuration).ToAudioSpecificConfig());

        private static MediaPacket Packet(MediaBufferAllocator allocator, byte[] data)
        {
            using WritableMediaBuffer writable = allocator.Allocate(data.Length);
            data.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new MediaPacket(0, buffer, new MediaTimestamp(123, new TimeBase(1, 48000)));
        }
    }
}
