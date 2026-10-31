// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class WaveformMixerTests
    {
        [Theory]
        [InlineData(AudioStorageLayout.Interleaved, 1)]
        [InlineData(AudioStorageLayout.Interleaved, 31)]
        [InlineData(AudioStorageLayout.Planar, 3)]
        [InlineData(AudioStorageLayout.Planar, 83)]
        public Task SuppliedDownmixPreservesQuantizedLevelsAcrossPacketBoundaries(AudioStorageLayout layout, int blockSize) =>
            WaveformMixerTestInput.VerifyAsync(layout, blockSize);

        [Fact]
        public void NonMonoMixerFailsBeforeReading()
        {
            MediaBufferAllocator allocator = new(16384);
            AudioSignalSource source = new(allocator, 400, 8, (_, _) => 0, [4]);
            AudioChannelMixer stereo = AudioDownmix.CreateStereo(source.Format);
            Assert.Throws<ArgumentException>(() => new AudioWaveformReader(source.ReadAsync, stereo,
                new AudioWaveformOptions(400, 4), allocator));
            Assert.Throws<ArgumentNullException>(() => new AudioWaveformReader(source.ReadAsync,
                (AudioChannelMixer)null!, new AudioWaveformOptions(400, 4), allocator));
            Assert.Equal(0, source.BlocksRead);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task SuppliedMixerFormatMismatchReleasesInputAndMakesReaderTerminal()
        {
            MediaBufferAllocator allocator = new(16384);
            AudioSignalSource source = new(allocator, 400, 8, (_, _) => 0.25, [4]);
            AudioFormat other = new(8000, AudioSampleFormat.Float64, AudioStorageLayout.Interleaved, "mono");
            AudioChannelMixer mixer = AudioDownmix.CreateMono(other);
            await using AudioWaveformReader reader = new(source.ReadAsync, mixer, new AudioWaveformOptions(400, 4), allocator);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await reader.ReadLevelAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadLevelAsync());
        }
    }
}
