// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers.Adts;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class AdtsProtectionTests
    {
        [Theory]
        [InlineData("silence-1", 1)]
        [InlineData("silence-2", 2)]
        [InlineData("tone-1", 1)]
        [InlineData("tone-2", 2)]
        [InlineData("data-element", 1)]
        public async Task ProtectedTransportRunsThroughPublicNativeSafeContracts(string name, int channels)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            await AdtsDecodeTestInput.VerifyProtectedAsync(root, name, channels, false);
            await AdtsDecodeTestInput.VerifyProtectedAsync(root, name, channels, true);
        }

        [Theory]
        [InlineData("silence-1")]
        [InlineData("silence-2")]
        [InlineData("tone-1")]
        [InlineData("tone-2")]
        [InlineData("data-element")]
        public async Task IndependentProtectedFramesValidateAndDecodeWithoutChangingPcm(string name)
        {
            byte[] input = await ReadFixtureAsync(name);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (ShortReadSource source = new(input))
            await using (AdtsReader reader = await AdtsReader.OpenAsync(source, allocator))
            {
                AacDecoderFactory factory = new();
                IPacketIntegrityValidator validator = factory.CreateIntegrityValidator(reader.Streams[0]);
                Assert.Equal(AdtsPacketProtection.SideDataKind, validator.SideDataKind);
                using IMediaCodec<MediaPacket, AudioFrame> protectedDecoder = factory.Create(reader.Streams[0], allocator);
                using IMediaCodec<MediaPacket, AudioFrame> rawDecoder = factory.Create(reader.Streams[0], allocator);
                int frames = 0;
                int position = 0;
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        AdtsFrameHeader header = AdtsFrameHeader.Read(input.AsSpan(position));
                        Assert.Equal(header.FrameBytes - 9, packet.Data.Length);
                        Assert.Equal(1, packet.SideDataCount);
                        Assert.Equal(PacketIntegrityStatus.Verified, validator.Validate(packet));
                        Assert.Equal(PacketIntegrityStatus.Verified, validator.Validate(packet));
                        Assert.True(MemoryMarshal.TryGetArray(packet.Data, out ArraySegment<byte> payload));
                        Assert.True(MemoryMarshal.TryGetArray(packet.GetSideData(0).Data, out ArraySegment<byte> prefix));
                        Assert.Same(payload.Array, prefix.Array);
                        Assert.Equal(prefix.Offset + 9, payload.Offset);
                        Assert.Equal(input.AsSpan(position, 9).ToArray(), packet.GetSideData(0).Data.ToArray());
                        using MediaBuffer raw = packet.RetainData();
                        using MediaPacket rawPacket = new(0, raw, packet.PresentationTime, packet.DecodeTime, packet.Duration);
                        Assert.Equal(PacketIntegrityStatus.NotPresent, validator.Validate(rawPacket));
                        protectedDecoder.TrySend(packet);
                        rawDecoder.TrySend(rawPacket);
                        using AudioFrame actual = protectedDecoder.Receive().Value;
                        using AudioFrame expected = rawDecoder.Receive().Value;
                        Assert.Equal(1024, actual.SamplesPerChannel);
                        Assert.Equal(expected.GetPlane(0).ToArray(), actual.GetPlane(0).ToArray());
                        position += header.FrameBytes;
                        frames++;
                    }
                }
                Assert.Equal(6, frames);
                Assert.Equal(input.Length, position);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("silence-1", 2, 2)]
        [InlineData("silence-1", 7, 1)]
        [InlineData("silence-1", 9, 16)]
        [InlineData("data-element", 42, 1)]
        public async Task ProtectedHeaderCrcWordAndSyntaxCorruptionFailInBothValidationPaths(string name, int offset, byte mask)
        {
            byte[] input = await ReadFixtureAsync(name);
            input[offset] ^= mask;
            MediaBufferAllocator allocator = new(256 * 1024);
            await using (ShortReadSource source = new(input))
            await using (AdtsReader reader = await AdtsReader.OpenAsync(source, allocator))
            {
                using MediaPacket packet = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                AacDecoderFactory factory = new();
                IPacketIntegrityValidator validator = factory.CreateIntegrityValidator(reader.Streams[0]);
                InvalidDataException failure = Assert.Throws<InvalidDataException>(() => validator.Validate(packet));
                Assert.Contains("CRC", failure.Message, StringComparison.Ordinal);
                Assert.Throws<InvalidDataException>(() => factory.CreateRandomAccessInspector(reader.Streams[0]).IsRandomAccess(packet));
                using IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(reader.Streams[0], allocator);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task IndexedProtectionReadsOnlyTheRequestedFrameAndSurvivesReaderDisposal()
        {
            byte[] input = await ReadFixtureAsync("tone-2");
            MediaBufferAllocator allocator = new(8192);
            MediaPacket retained;
            IPacketIntegrityValidator validator;
            await using (AdtsReadTrackingSource source = new(input))
            await using (AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator))
            {
                Assert.Equal(42, source.Reads.Sum(read => read.Length));
                source.Reads.Clear();
                validator = new AacDecoderFactory().CreateIntegrityValidator(reader.Streams[0]);
                long offset = 0;
                for (int index = 0; index < 4; index++)
                {
                    offset += AdtsFrameHeader.Read(input.AsSpan((int)offset)).FrameBytes;
                }
                int bytes = AdtsFrameHeader.Read(input.AsSpan((int)offset)).FrameBytes;
                retained = await reader.ReadPacketAtAsync(0, 4);
                Assert.Equal(bytes, source.Reads.Sum(read => read.Length));
                Assert.Equal(offset, source.Reads[0].Offset);
                Assert.Equal(bytes, allocator.AllocatedBytes);
                Assert.Equal(0, source.Position);
            }
            using (retained)
            {
                Assert.Equal(PacketIntegrityStatus.Verified, validator.Validate(retained));
                using MediaPacket repeated = retained.Retain();
                Assert.Equal(PacketIntegrityStatus.Verified, validator.Validate(repeated));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ProtectedReadsHonorBudgetAndCancellationWithoutLeakingStorage()
        {
            byte[] input = await ReadFixtureAsync("tone-2");
            int frameBytes = AdtsFrameHeader.Read(input).FrameBytes;
            MediaBufferAllocator allocator = new(frameBytes + 8);
            await using (ShortReadSource source = new(input))
            await using (AdtsReader reader = await AdtsReader.OpenAsync(source, allocator))
            {
                await Assert.ThrowsAsync<MediaMemoryLimitException>(() => reader.ReadPacketAsync().AsTask());
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            MediaBufferAllocator cancelled = new(8192);
            await using (GatedByteSource source = new(input))
            await using (AdtsReader reader = await AdtsReader.OpenAsync(source, cancelled))
            {
                source.GateReads = true;
                using CancellationTokenSource cancellation = new();
                Task<MediaPacket?> pending = reader.ReadPacketAsync(cancellation.Token).AsTask();
                await source.Entered.Task;
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.Equal(0, cancelled.AllocatedBytes);
            }
        }

        [Fact]
        public async Task IntegrityCapabilityRejectsMalformedSideDataAndHonorsCancellationAndLimits()
        {
            byte[] input = await ReadFixtureAsync("tone-1");
            MediaBufferAllocator allocator = new(8192);
            await using (ShortReadSource source = new(input))
            await using (AdtsReader reader = await AdtsReader.OpenAsync(source, allocator))
            {
                using MediaPacket packet = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                IPacketIntegrityValidator validator = new AacDecoderFactory().CreateIntegrityValidator(reader.Streams[0]);
                using CancellationTokenSource cancellation = new();
                await cancellation.CancelAsync();
                Assert.Throws<OperationCanceledException>(() => validator.Validate(packet, cancellation.Token));
                IPacketIntegrityValidator limited = new AacDecoderFactory(1).CreateIntegrityValidator(reader.Streams[0]);
                Assert.Throws<NotSupportedException>(() => limited.Validate(packet));
                using MediaBuffer raw = packet.RetainData();
                using MediaPacket wrongStream = new(1, raw);
                Assert.Throws<ArgumentException>(() => validator.Validate(wrongStream));
                using MediaBuffer empty = raw.Slice(0, 0);
                using MediaPacketSideData side = new(AdtsPacketProtection.SideDataKind, empty);
                using MediaPacket malformed = new(0, raw, sideData: [side]);
                Assert.Throws<InvalidDataException>(() => validator.Validate(malformed));
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(reader.Streams[0], allocator);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(malformed));
                Assert.Throws<InvalidDataException>(() => new AacDecoderFactory().CreateRandomAccessInspector(reader.Streams[0])
                    .IsRandomAccess(malformed));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static Task<byte[]> ReadFixtureAsync(string name) =>
            File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", name + ".aac"));
    }
}
