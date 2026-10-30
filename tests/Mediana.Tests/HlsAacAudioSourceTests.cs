// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class HlsAacAudioSourceTests
    {
        [Theory]
        [InlineData("avc-aac.mp4", 48000, 76800, 76)]
        [InlineData("avc-mp3-44.mp4", 44100, 317520, 312)]
        [InlineData("avc-mp3-22.mp4", 22050, 158760, 157)]
        public async Task EditedInputKeepsPrimingBeforeVideoTiesAndDrainsEverySample(string name, int rate, long samples, long packets)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Data("Mp4", name)));
            await using HlsAacAudioSource audio = await HlsAacAudioSource.OpenAsync(source, 1, 128000, allocator, default);
            await audio.WriteBeforeAsync(new(0, new(1, rate)), [], default);
            Assert.Equal(1, audio.PacketsWritten);
            await audio.WriteBeforeAsync(new(0, new(1, rate)), [], default);
            Assert.Equal(1, audio.PacketsWritten);
            await audio.WriteBeforeAsync(new(1024, new(1, rate)), [], default);
            Assert.Equal(2, audio.PacketsWritten);
            await audio.WriteBeforeAsync(null, [], default);
            Assert.Equal(packets, audio.PacketsWritten);
            Assert.Equal(samples, audio.InputSamplesPerChannel);
            Assert.Equal(1024, audio.PrimingSamples);
            Assert.Equal((int)((1024 - samples % 1024) % 1024), audio.PaddingSamples);
            await audio.WriteBeforeAsync(null, [], default);
            Assert.Equal(packets, audio.PacketsWritten);
            await audio.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FractionalSampleMovieDelayIsExactAndFiniteEditClipsTheInputTail()
        {
            byte[] data = Mp4EditTestData.Replace(await File.ReadAllBytesAsync(Data("Aac", "lc-stereo-44.m4a")), false,
                (1, -1, 65536), (10, 1025, 65536));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using HlsAacAudioSource audio = await HlsAacAudioSource.OpenAsync(source, 0, 128000, allocator, default);
            Assert.Equal(new TimeBase(1, 441000), audio.Stream.TimeBase);
            MediaTimestamp primingStart = new(441 - 10240, new(1, 441000));
            await audio.WriteBeforeAsync(primingStart, [], default);
            Assert.Equal(0, audio.PacketsWritten);
            await audio.WriteBeforeAsync(new(1, new(1, 1000)), [], default);
            Assert.Equal(1, audio.PacketsWritten);
            await audio.WriteBeforeAsync(null, [], default);
            Assert.Equal(441, audio.InputSamplesPerChannel);
            Assert.Equal(2, audio.PacketsWritten);
            Assert.Equal(583, audio.PaddingSamples);
            await audio.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MissingEditUsesAllDecodedSamplesWithoutInventingDelayTrimming()
        {
            byte[] data = Mp4EditTestData.Replace(await File.ReadAllBytesAsync(Data("Aac", "lc-mono-48.m4a")), false);
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using HlsAacAudioSource audio = await HlsAacAudioSource.OpenAsync(source, 0, 64000, allocator, default);
            await audio.WriteBeforeAsync(null, [], default);
            Assert.Equal(19456, audio.InputSamplesPerChannel);
            Assert.Equal(20, audio.PacketsWritten);
            Assert.Equal(0, audio.PaddingSamples);
            await audio.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DisposalReleasesPreparedPcmAndPendingPacketEvenAfterCancellation(bool read)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Data("Mp4", "avc-aac.mp4")));
            await using HlsAacAudioSource audio = await HlsAacAudioSource.OpenAsync(source, 1, 128000, allocator, default);
            if (read)
            {
                await audio.WriteBeforeAsync(new(-1, new(1, 1)), [], default);
            }
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => audio.WriteBeforeAsync(null, [], cancellation.Token).AsTask());
            await audio.DisposeAsync();
            await audio.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5000000)]
        public async Task UnsupportedBitBudgetReleasesEveryOpenedResource(int rate)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Data("Mp4", "avc-aac.mp4")));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => HlsAacAudioSource.OpenAsync(source, 1, rate, allocator, default).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static string Data(string group, string name) => Path.Combine(AppContext.BaseDirectory, "Data", group, name);
    }
}
