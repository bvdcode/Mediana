// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Ogg;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Codecs.Vorbis.Tests
{
    public class VorbisDecoderContractTests
    {
        [Fact]
        public async Task ResetFailureCancellationAndRetainedPcmRespectOwnership()
        {
            (EncodedStreamInfo stream, byte[] firstBytes, byte[] secondBytes) = await ReadInputAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new VorbisDecoderFactory().Create(stream, allocator);
            using MediaPacket first = Packet(firstBytes, allocator), second = Packet(secondBytes, allocator);
            decoder.TrySend(first); Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            decoder.TrySend(second); using AudioFrame retained = decoder.Receive().Value;
            byte[] expected = retained.GetPlane(0).ToArray();
            using MediaBuffer retainedPacket = second.RetainData();
            using MediaPacket invalid = new(0, retainedPacket, duration: new(999999, stream.TimeBase));
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(invalid));
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset(); decoder.TrySend(first); decoder.TrySend(second);
            using AudioFrame recovered = decoder.Receive().Value;
            Assert.Equal(expected, recovered.GetPlane(0).ToArray());
            Assert.Throws<OperationCanceledException>(() => decoder.TrySend(first, new(true)));
            decoder.Reset(); decoder.TrySend(first); decoder.TrySend(second);
            using AudioFrame afterCancellation = decoder.Receive().Value;
            decoder.Dispose();
            Assert.Equal(expected, retained.GetPlane(0).ToArray());
            Assert.Equal(expected, afterCancellation.GetPlane(0).ToArray());
        }

        [Fact]
        public async Task BudgetFailureReleasesPartiallyDecodedSetupAndTransformPlans()
        {
            (EncodedStreamInfo stream, byte[] firstBytes, byte[] _) = await ReadInputAsync();
            VorbisConfiguration configuration = VorbisConfiguration.Read(stream.Configuration);
            MediaBufferAllocator measurement = new(1024 * 1024);
            long required;
            using (VorbisFrameDecoder frame = new(configuration.Identification, configuration.SetupHeader.Span, measurement))
            {
                required = measurement.AllocatedBytes;
            }
            Assert.Equal(0, measurement.AllocatedBytes);
            foreach (long available in new[] { 1, required / 4, required / 2, required - 1 })
            {
                MediaBufferAllocator allocator = new(firstBytes.Length + available);
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new VorbisDecoderFactory().Create(stream, allocator);
                using MediaPacket packet = Packet(firstBytes, allocator);
                Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
                Assert.Equal(firstBytes.Length, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task RepeatedResetsReusePreparedTablesAndWorkingStorage()
        {
            (EncodedStreamInfo stream, byte[] firstBytes, byte[] secondBytes) = await ReadInputAsync();
            VorbisConfiguration configuration = VorbisConfiguration.Read(stream.Configuration);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VorbisFrameDecoder decoder = new(configuration.Identification, configuration.SetupHeader.Span, allocator);
            float[] output = new float[decoder.MaximumFrameSamples * configuration.Identification.ChannelCount];
            for (int pass = 0; pass < 4; pass++) { Decode(decoder, firstBytes, secondBytes, output); }
            long before = GC.GetAllocatedBytesForCurrentThread(), reserved = allocator.AllocatedBytes;
            for (int pass = 0; pass < 32; pass++) { Decode(decoder, firstBytes, secondBytes, output); }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread()); Assert.Equal(reserved, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancelledFrameDoesNotChangeTheOverlapState()
        {
            (EncodedStreamInfo stream, byte[] firstBytes, byte[] secondBytes) = await ReadInputAsync();
            VorbisConfiguration configuration = VorbisConfiguration.Read(stream.Configuration);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VorbisFrameDecoder decoder = new(configuration.Identification, configuration.SetupHeader.Span, allocator);
            float[] output = new float[decoder.MaximumFrameSamples * configuration.Identification.ChannelCount];
            decoder.Decode(firstBytes, output);
            int samples = decoder.GetOutputSamples(secondBytes);
            Assert.Throws<OperationCanceledException>(() => decoder.Decode(secondBytes, output, new(true)));
            Assert.Equal(samples, decoder.GetOutputSamples(secondBytes));
            Assert.Equal(samples, decoder.Decode(secondBytes, output));
        }

        private static void Decode(VorbisFrameDecoder decoder, byte[] first, byte[] second, float[] output)
        {
            decoder.Reset(); decoder.Decode(first, output); decoder.Decode(second, output);
        }

        private static MediaPacket Packet(byte[] bytes, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length); bytes.CopyTo(writable.Span);
            using MediaBuffer buffer = writable.Publish();
            return new(0, buffer);
        }

        private static async Task<(EncodedStreamInfo Stream, byte[] First, byte[] Second)> ReadInputAsync()
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "vorbis-stereo.ogg"));
            await using RandomAccessStreamByteSource source = new(new MemoryStream(bytes));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggReader reader = await OggReader.OpenAsync(source, allocator);
            using MediaPacket first = await reader.ReadPacketAtAsync(0, 0), second = await reader.ReadPacketAtAsync(0, 1);
            return (reader.Streams[0], first.Data.ToArray(), second.Data.ToArray());
        }
    }
}
