// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Wave;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    public class WaveWriterTests
    {
        [Theory]
        [InlineData(AudioSampleFormat.Unsigned8)]
        [InlineData(AudioSampleFormat.Signed16)]
        [InlineData(AudioSampleFormat.Signed32)]
        [InlineData(AudioSampleFormat.Float32)]
        [InlineData(AudioSampleFormat.Float64)]
        public async Task CompleteWritesValidSizesExactSamplesAndFactForFloat(AudioSampleFormat sampleFormat)
        {
            AudioFormat format = new(48000, sampleFormat, AudioStorageLayout.Interleaved, "mono");
            MediaBufferAllocator allocator = new(8192);
            byte[] bytes = Enumerable.Range(0, format.BytesPerSample * 3).Select(index => (byte)(index + 1)).ToArray();
            using AudioFrame frame = Frame(format, bytes, allocator);
            using MemoryStream output = new();
            await using (WavePcmWriter writer = await WavePcmWriter.OpenAsync(output, format, allocator))
            {
                await writer.WriteFrameAsync(frame);
                await writer.WriteFrameAsync(frame);
                await writer.CompleteAsync();
                Assert.Equal(6, writer.SamplesPerChannel);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.WriteFrameAsync(frame));
            }
            Assert.True(output.CanWrite);
            byte[] file = output.ToArray();
            Assert.Equal(file.Length - 8, (long)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)));
            int formatLength = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(16));
            if (sampleFormat is AudioSampleFormat.Float32 or AudioSampleFormat.Float64)
            {
                Assert.Equal("fact"u8.ToArray(), file.AsSpan(20 + formatLength, 4).ToArray());
                Assert.Equal(6U, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(28 + formatLength)));
            }
            await using ShortReadSource source = new(file);
            await using (WaveReader reader = await WaveReader.OpenAsync(source, allocator))
            {
                Assert.Equal(6, reader.SamplesPerChannel);
                Assert.Equal(sampleFormat, reader.Configuration.Format.SampleFormat);
                using MediaPacket packet = (await reader.ReadPacketAsync())!;
                byte[] expected = [.. bytes, .. bytes];
                if (!BitConverter.IsLittleEndian)
                {
                    for (int offset = 0; offset < expected.Length; offset += format.BytesPerSample)
                    {
                        expected.AsSpan(offset, format.BytesPerSample).Reverse();
                    }
                }
                Assert.Equal(expected, packet.Data.ToArray());
                Assert.Null(await reader.ReadPacketAsync());
            }
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task OddPcmDataIsPaddedWithoutCountingPaddingAsSamples()
        {
            AudioFormat format = new(8000, AudioSampleFormat.Unsigned8, AudioStorageLayout.Interleaved, "mono");
            MediaBufferAllocator allocator = new(8192);
            using AudioFrame frame = Frame(format, [128, 192, 255], allocator);
            using MemoryStream output = new();
            await using WavePcmWriter writer = await WavePcmWriter.OpenAsync(output, format, allocator);
            await writer.WriteFrameAsync(frame);
            await writer.CompleteAsync();
            byte[] file = output.ToArray();
            Assert.Equal(48, file.Length);
            Assert.Equal(3U, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(40)));
            Assert.Equal(0, file[^1]);
            await using ShortReadSource source = new(file);
            await using WaveReader reader = await WaveReader.OpenAsync(source, allocator);
            Assert.Equal(3, reader.SamplesPerChannel);
            using MediaPacket packet = (await reader.ReadPacketAsync())!;
            Assert.Equal(new byte[] { 128, 192, 255 }, packet.Data.ToArray());
            Assert.Null(await reader.ReadPacketAsync());
        }

        [Fact]
        public void ExtensibleFormatPreservesSpeakerMaskAndRejectsReorderedOrMixedLabels()
        {
            string[] channels = ["front-left", "front-right", "front-center", "low-frequency", "side-left", "side-right"];
            AudioFormat format = new(48000, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, channels);
            byte[] encoded = PcmConfiguration.WriteWaveFormat(format);
            Assert.Equal(40, encoded.Length);
            Assert.Equal(0x60fU, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(20)));
            PcmConfiguration decoded = PcmConfiguration.ReadWaveFormat(encoded, out _)!;
            Assert.Equal(channels, decoded.Format.Channels);
            foreach (string[] invalid in new string[][] { ["front-right", "front-left"], ["front-left", "discrete-2"], ["unknown"] })
            {
                AudioFormat unsupported = new(48000, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, invalid);
                Assert.Throws<NotSupportedException>(() => PcmConfiguration.WriteWaveFormat(unsupported));
            }
            AudioFormat discrete = new(44100, AudioSampleFormat.Signed32, AudioStorageLayout.Interleaved, "discrete-1", "discrete-2", "discrete-3");
            PcmConfiguration parsed = PcmConfiguration.ReadWaveFormat(PcmConfiguration.WriteWaveFormat(discrete), out _)!;
            Assert.Equal(discrete.Channels, parsed.Format.Channels);
        }

        [Fact]
        public async Task CancellationFaultsWriterAndDisposalDoesNotFinalizeOrCloseDestination()
        {
            AudioFormat format = new(8000, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, "mono");
            MediaBufferAllocator allocator = new(8192);
            using AudioFrame frame = Frame(format, [1, 2], allocator);
            using MemoryStream output = new();
            WavePcmWriter writer = await WavePcmWriter.OpenAsync(output, format, allocator);
            using CancellationTokenSource token = new();
            await token.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writer.WriteFrameAsync(frame, token.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.CompleteAsync());
            await writer.DisposeAsync();
            Assert.True(output.CanWrite);
            Assert.Equal(0U, BinaryPrimitives.ReadUInt32LittleEndian(output.ToArray().AsSpan(4)));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await writer.WriteFrameAsync(frame));
        }

        [Fact]
        public async Task ChangedFormatIsRejectedBeforeWritingAnySamples()
        {
            AudioFormat configured = new(8000, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, "mono");
            AudioFormat changed = new(16000, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, "mono");
            MediaBufferAllocator allocator = new(8192);
            using AudioFrame frame = Frame(changed, [1, 2], allocator);
            using MemoryStream output = new();
            await using WavePcmWriter writer = await WavePcmWriter.OpenAsync(output, configured, allocator);
            long before = output.Length;
            await Assert.ThrowsAsync<NotSupportedException>(async () => await writer.WriteFrameAsync(frame));
            Assert.Equal(before, output.Length);
        }

        private static AudioFrame Frame(AudioFormat format, byte[] bytes, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory);
            using MediaBuffer buffer = writable.Publish();
            return new(format, bytes.Length / format.Channels.Count / format.BytesPerSample, buffer);
        }
    }
}
