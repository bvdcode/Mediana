// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class HlsAacVariantSourceTests
    {
        [Fact]
        public async Task MultipleRatesReadAndDecodeTheInputOnceAndReuseEqualSettings()
        {
            byte[] data = await File.ReadAllBytesAsync(Data());
            await using TrackingRandomAccessSource singleInput = new(data);
            await using TrackingRandomAccessSource groupedInput = new(data);
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using (HlsAacAudioSource single = await HlsAacAudioSource.OpenAsync(singleInput, 1, [128000], null, allocator, default))
            {
                await single.WriteBeforeAsync(null, [], default);
                Assert.Equal(76, single.PacketsWritten);
            }
            await using (HlsAacAudioSource grouped = await HlsAacAudioSource.OpenAsync(groupedInput, 1,
                [96000, 128000, 96000, 192000], null, allocator, default))
            {
                Assert.Equal(3, grouped.EncoderCount);
                Assert.Same(grouped.GetStream(0), grouped.GetStream(2));
                Assert.NotSame(grouped.GetStream(0), grouped.GetStream(1));
                await grouped.WriteBeforeAsync(new(0, new(1, 48000)), [], default);
                Assert.Equal(3, grouped.PacketsWritten);
                await grouped.WriteBeforeAsync(new(0, new(1, 48000)), [], default);
                Assert.Equal(3, grouped.PacketsWritten);
                await grouped.WriteBeforeAsync(null, [], default);
                Assert.Equal(228, grouped.PacketsWritten);
                Assert.Equal(76800, grouped.InputSamplesPerChannel);
                Assert.Equal(1024, grouped.PrimingSamples);
                Assert.Equal(0, grouped.PaddingSamples);
            }
            Assert.Equal(singleInput.Reads, groupedInput.Reads);
            Assert.False(singleInput.IsDisposed);
            Assert.False(groupedInput.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailedLaterEncoderAndCancellationReleaseEveryVariant(bool cancel)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource input = new(await File.ReadAllBytesAsync(Data()));
            if (cancel)
            {
                await using HlsAacAudioSource grouped = await HlsAacAudioSource.OpenAsync(input, 1,
                    [96000, 128000, 192000], null, allocator, default);
                await grouped.WriteBeforeAsync(new(-1, new(1, 1)), [], default);
                Assert.Equal(0, grouped.PacketsWritten);
                Assert.True(allocator.AllocatedBytes > 0);
                using CancellationTokenSource cancellation = new();
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await grouped.WriteBeforeAsync(null, [], cancellation.Token));
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await grouped.WriteBeforeAsync(null, [], default));
                await grouped.DisposeAsync();
            }
            else
            {
                await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
                    await HlsAacAudioSource.OpenAsync(input, 1, [128000, 5000000], null, allocator, default));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(input.IsDisposed);
        }

        private static string Data() => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4");
    }
}
