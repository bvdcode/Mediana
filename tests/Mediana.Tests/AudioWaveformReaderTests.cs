// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioWaveformReaderTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(6)]
        [InlineData(1024)]
        public async Task MonoMixPreservesExactWindowTimingAndTheFinalRemainder(int window)
        {
            MediaBufferAllocator allocator = new(65536);
            AudioSignalSource source = new(allocator, 8000, 23, (_, channel) => channel switch
            {
                0 => 0.5,
                1 => -0.25,
                _ => throw new ArgumentOutOfRangeException(nameof(channel))
            }, [1, 7, 3], AudioStorageLayout.Planar, ["left", "right"]);
            double[] gains = [0.5, -0.5];
            await using AudioWaveformReader reader = new(source.ReadAsync, gains,
                new AudioWaveformOptions(8000, window) { SampleFormat = AudioSampleFormat.Signed16 }, allocator);
            gains[0] = 100;
            int samples = 0;
            while (await reader.ReadLevelAsync() is AudioLevel level)
            {
                Assert.Equal(samples, level.StartSample);
                Assert.Equal(Math.Min(window, 23 - samples), level.SampleCount);
                Assert.Equal(new MediaTimestamp(samples - 7, new TimeBase(1, 8000)), level.Timestamp);
                Assert.Equal(new MediaDuration(level.SampleCount, new TimeBase(1, 8000)), level.Duration);
                Assert.Equal(0.375, level.RootMeanSquare);
                Assert.Equal(0.375, level.Peak);
                samples += level.SampleCount;
            }
            Assert.Equal(23, samples);
            Assert.Null(await reader.ReadLevelAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(AudioSampleFormat.Float64)]
        [InlineData(AudioSampleFormat.Signed16)]
        public async Task QuantizationPrecedesAnalysisAndFloatingPointPreservesHeadroom(AudioSampleFormat precision)
        {
            MediaBufferAllocator allocator = new(16384);
            AudioSignalSource source = new(allocator, 400, 2, (index, _) => index switch
            {
                0 => 0.1,
                1 => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            }, [2]);
            await using AudioWaveformReader reader = new(source.ReadAsync, [1],
                new AudioWaveformOptions(400, 2) { SampleFormat = precision }, allocator);
            AudioLevel level = (await reader.ReadLevelAsync())!.Value;
            (double first, double second) = precision switch
            {
                AudioSampleFormat.Float64 => (0.1, 2),
                AudioSampleFormat.Signed16 => (3277 / 32768d, 32767 / 32768d),
                _ => throw new ArgumentOutOfRangeException(nameof(precision))
            };
            Assert.InRange(Math.Abs(level.RootMeanSquare - Math.Sqrt((first * first + second * second) / 2)), 0, 1e-15);
            Assert.Equal(second, level.Peak);
            Assert.Null(await reader.ReadLevelAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task EmptyInputAndUnknownTimestampsRemainExplicit()
        {
            MediaBufferAllocator allocator = new(16384);
            AudioSignalSource empty = new(allocator, 8000, 0, (_, _) => 0, [1]);
            await using (AudioWaveformReader reader = new(empty.ReadAsync, [1], new AudioWaveformOptions(400, 40), allocator))
            {
                Assert.Null(await reader.ReadLevelAsync());
                Assert.Null(await reader.ReadLevelAsync());
            }
            AudioSignalSource source = new(allocator, 400, 5, (_, _) => 0.25, [2]) { Timestamp = _ => null };
            await using (AudioWaveformReader reader = new(source.ReadAsync, [1], new AudioWaveformOptions(400, 3), allocator))
            {
                Assert.Null((await reader.ReadLevelAsync())!.Value.Timestamp);
                Assert.Null((await reader.ReadLevelAsync())!.Value.Timestamp);
                Assert.Null(await reader.ReadLevelAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ALongerRecordingUsesBoundedStorageAndProducesAllLevels()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            AudioSignalSource source = new(allocator, 48000, 480000, (_, _) => 0.5, [127],
                AudioStorageLayout.Planar, ["left", "right"]);
            await using AudioWaveformReader reader = new(source.ReadAsync, [0.5, 0.5], new AudioWaveformOptions(400, 40)
            {
                Resampling = new AudioResampleOptions { BoundaryMode = AudioBoundaryMode.Reflect }
            }, allocator);
            int windows = 0;
            while (await reader.ReadLevelAsync() is AudioLevel level)
            {
                Assert.Equal(windows * 40, level.StartSample);
                Assert.Equal(40, level.SampleCount);
                Assert.InRange(Math.Abs(level.RootMeanSquare - 0.5), 0, 2e-12);
                Assert.InRange(Math.Abs(level.Peak - 0.5), 0, 2e-12);
                windows++;
            }
            Assert.Equal(100, windows);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationAfterAWindowReleasesLookaheadAndMakesTheReaderTerminal()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            AudioSignalSource source = new(allocator, 48000, 48000, (_, _) => 0.5, [127]);
            await using AudioWaveformReader reader = new(source.ReadAsync, [1], new AudioWaveformOptions(400, 1), allocator);
            AudioLevel first = (await reader.ReadLevelAsync())!.Value;
            Assert.True(allocator.AllocatedBytes > 0);
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadLevelAsync(cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadLevelAsync());
            Assert.Equal(1, first.SampleCount);
            using AudioFrame? borrowedSourceFrame = await source.ReadAsync(CancellationToken.None);
            Assert.NotNull(borrowedSourceFrame);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InvalidInputOrAllocationFailureReleasesOwnedFrames(bool insufficientBudget)
        {
            MediaBufferAllocator sourceAllocator = new(65536);
            MediaBufferAllocator processing = new(insufficientBudget ? 1 : 65536);
            AudioSignalSource source = new(sourceAllocator, 400, 5, (_, _) => 1, [3]);
            double[] gains = insufficientBudget ? [1] : [0.5, 0.5];
            await using AudioWaveformReader reader = new(source.ReadAsync, gains, new AudioWaveformOptions(400, 2), processing);
            if (insufficientBudget)
            {
                await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await reader.ReadLevelAsync());
            }
            else
            {
                await Assert.ThrowsAsync<ArgumentException>(async () => await reader.ReadLevelAsync());
            }
            Assert.Equal(0, sourceAllocator.AllocatedBytes);
            Assert.Equal(0, processing.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadLevelAsync());
        }

        [Fact]
        public async Task ConcurrentReadsAndDisposalCannotInterruptAnActiveRead()
        {
            MediaBufferAllocator allocator = new(65536);
            AudioSignalSource source = new(allocator, 400, 8, (_, _) => 0.25, [4]);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            async ValueTask<AudioFrame?> ReadAsync(CancellationToken cancellationToken)
            {
                entered.TrySetResult();
                await proceed.Task.WaitAsync(cancellationToken);
                return await source.ReadAsync(cancellationToken);
            }
            await using AudioWaveformReader reader = new(ReadAsync, [1], new AudioWaveformOptions(400, 4), allocator);
            Task<AudioLevel?> pending = reader.ReadLevelAsync().AsTask();
            await entered.Task;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadLevelAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.DisposeAsync());
            proceed.SetResult();
            AudioLevel level = (await pending)!.Value;
            await reader.DisposeAsync();
            await reader.DisposeAsync();
            Assert.Equal(0.25, level.RootMeanSquare);
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await reader.ReadLevelAsync());
        }

        [Fact]
        public void InvalidOptionsAndGainsFailBeforeReadingTheSource()
        {
            MediaBufferAllocator allocator = new(65536);
            AudioSignalSource source = new(allocator, 400, 8, (_, _) => 0, [4]);
            foreach (AudioWaveformOptions options in new[]
            {
                new AudioWaveformOptions(0, 40), new AudioWaveformOptions(400, 0),
                new AudioWaveformOptions(400, 40) { SampleFormat = (AudioSampleFormat)99 },
                new AudioWaveformOptions(400, 40) { Resampling = new AudioResampleOptions { SamplesPerFrame = 0 } }
            })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new AudioWaveformReader(source.ReadAsync, [1], options, allocator));
            }
            foreach (double[] gains in new double[][] { [], [double.NaN], [double.PositiveInfinity] })
            {
                Assert.Throws<ArgumentException>(() => new AudioWaveformReader(source.ReadAsync, gains, new AudioWaveformOptions(400, 40), allocator));
            }
            Assert.Equal(0, source.BlocksRead);
        }
    }
}
