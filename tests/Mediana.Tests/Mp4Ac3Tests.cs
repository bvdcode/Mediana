// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp4Ac3Tests
    {
        [Theory]
        [InlineData("mono.mp4", "mono.ac3", false, 1)]
        [InlineData("stereo-eac3.mp4", "stereo.eac3", true, 2)]
        public Task FullPipelineProbeAndDecodePreserveEverySampleOfIndependentRemuxedInput(
            string containerName, string elementaryName, bool enhanced, int channels) =>
            Ac3ContainerTestInput.VerifyAsync(containerName, elementaryName, enhanced, channels);

        [Fact]
        public Task BundledOneBlockSyncframesAndIndexedPrerollMatchContinuousPcmExactly() =>
            Ac3ContainerTestInput.VerifyBundledAndSeekAsync();

        [Fact]
        public Task DelayedTransientProcessingDrainsThroughContinuousSeekingAndHlsAudioPreparation() =>
            Ac3ContainerTestInput.VerifyTransientAsync();

        [Theory]
        [InlineData("mono.mp4", false, 1)]
        [InlineData("stereo-eac3.mp4", true, 2)]
        public async Task AudioSampleEntryDoesNotOverrideSpecificBoxChannelCountOrSamplingRate(string name, bool enhanced, int channels)
        {
            byte[] bytes = await ReadAsync(name);
            int type = bytes.AsSpan().IndexOf(enhanced ? "ec-3"u8 : "ac-3"u8);
            Assert.True(type >= 0);
            bytes.AsSpan(type + 20, 4).Clear();
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(type + 28, 4), 12345);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(bytes);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(channels, reader.Tracks[0].DeclaredAudioChannels);
                Assert.Equal(48000, reader.Tracks[0].DeclaredAudioSampleRate);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("mono.mp4", false)]
        [InlineData("stereo-eac3.mp4", true)]
        public async Task MalformedSpecificBoxFailsWithoutLeakingMovieStorage(string name, bool enhanced)
        {
            byte[] bytes = await ReadAsync(name);
            int type = bytes.AsSpan().IndexOf(enhanced ? "dec3"u8 : "dac3"u8);
            Assert.True(type >= 0);
            bytes[type + 6] |= 1;
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(bytes);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await Mp4Reader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static Task<byte[]> ReadAsync(string name) => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", name));
    }
}
