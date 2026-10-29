using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioResamplerTests
    {
        [Theory]
        [InlineData(48000, 400, AudioStorageLayout.Interleaved)]
        [InlineData(44100, 400, AudioStorageLayout.Planar)]
        [InlineData(8000, 11025, AudioStorageLayout.Interleaved)]
        [InlineData(48000, 48000, AudioStorageLayout.Planar)]
        public Task BandlimitedTonesPreserveAmplitudeChannelsSampleCountAndExactTime(int inputRate, int outputRate, AudioStorageLayout layout) =>
            AudioResamplingTestInput.VerifyAsync(inputRate, outputRate, layout);

        [Fact]
        public async Task FrequenciesAboveTheNewNyquistAreRemovedBeforeDecimation()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioSignalSource source = new(allocator, 48000, 48000,
                (index, _) => Math.Sin(2 * Math.PI * 1001 * index / 48000), [101]);
            await using AudioResampler resampler = new(source.ReadAsync, 400, allocator);
            using AudioFrame output = (await resampler.ReadFrameAsync())!;
            ReadOnlySpan<double> samples = MemoryMarshal.Cast<byte, double>(output.GetPlane(0).Span);
            double energy = 0;
            for (int index = 40; index < 360; index++)
            {
                energy += samples[index] * samples[index];
            }
            Assert.True(Math.Sqrt(energy / 320) < 0.00001);
            Assert.Null(await resampler.ReadFrameAsync());
        }

        [Theory]
        [InlineData(1, 17)]
        [InlineData(2, 400)]
        [InlineData(4096, 3)]
        public async Task PacketAndOutputBoundariesDoNotChangeAnySample(int inputBlock, int outputBlock)
        {
            double[] expected = await ConvertAsync(113, 73);
            double[] actual = await ConvertAsync(inputBlock, outputBlock);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task UnityRateIsExactAndAOneSampleInputFlushesTheDeclaredDuration()
        {
            MediaBufferAllocator allocator = new(32768);
            AudioSignalSource source = new(allocator, 400, 9, (index, _) => index - 4, [2]);
            await using (AudioResampler identity = new(source.ReadAsync, 400, allocator))
            using (AudioFrame frame = (await identity.ReadFrameAsync())!)
            {
                Assert.Equal(new double[] { -4, -3, -2, -1, 0, 1, 2, 3, 4 }, MemoryMarshal.Cast<byte, double>(frame.GetPlane(0).Span).ToArray());
                Assert.Null(await identity.ReadFrameAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            AudioSignalSource one = new(allocator, 400, 1, (_, _) => 1, [1]);
            await using (AudioResampler upsample = new(one.ReadAsync, 800, allocator, new AudioResampleOptions { SamplesPerFrame = 1 }))
            {
                using AudioFrame first = (await upsample.ReadFrameAsync())!;
                using AudioFrame second = (await upsample.ReadFrameAsync())!;
                Assert.Equal(1, first.SamplesPerChannel);
                Assert.Equal(1, second.SamplesPerChannel);
                Assert.True(MemoryMarshal.Read<double>(first.GetPlane(0).Span) > MemoryMarshal.Read<double>(second.GetPlane(0).Span));
                Assert.Null(await upsample.ReadFrameAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UnknownTimestampsRemainUnknownAndDiscontinuitiesFail(bool discontinuous)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioSignalSource source = new(allocator, 48000, 5000, (_, _) => 0.25, [100]);
            if (discontinuous)
            {
                source.Timestamp = position => new MediaTimestamp(position * 2, new TimeBase(1, 48000));
            }
            else
            {
                source.Timestamp = _ => null;
            }
            await using AudioResampler resampler = new(source.ReadAsync, 400, allocator);
            if (discontinuous)
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () => await resampler.ReadFrameAsync());
                Assert.Equal(0, allocator.AllocatedBytes);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await resampler.ReadFrameAsync());
            }
            else
            {
                using AudioFrame output = (await resampler.ReadFrameAsync())!;
                Assert.Null(output.Timestamp);
                Assert.Equal(42, output.SamplesPerChannel);
            }
        }

        [Fact]
        public async Task CancellationAndFilterOrBufferLimitsReleaseInputAndHistory()
        {
            foreach (int mode in new[] { 0, 1, 2, 3 })
            {
                MediaBufferAllocator allocator = new(1024 * 1024);
                AudioSignalSource source = new(allocator, 48000, 48000, (_, _) => 0, [1]);
                AudioResampleOptions options = new() { MaxCoefficientBytes = 8 };
                MediaBufferAllocator processing = allocator;
                if (mode == 1)
                {
                    options = new AudioResampleOptions();
                    processing = new MediaBufferAllocator(1);
                }
                if (mode >= 2)
                {
                    options = new AudioResampleOptions();
                }
                using CancellationTokenSource cancellation = new();
                async ValueTask<AudioFrame?> Read(CancellationToken token)
                {
                    AudioFrame? frame = await source.ReadAsync(token);
                    if (mode == 3)
                    {
                        await cancellation.CancelAsync();
                    }
                    return frame;
                }
                await using AudioResampler resampler = new(Read, 400, processing, options);
                if (mode == 2)
                {
                    await cancellation.CancelAsync();
                }
                switch (mode)
                {
                    case 0:
                        await Assert.ThrowsAsync<NotSupportedException>(async () => await resampler.ReadFrameAsync());
                        break;
                    case 1:
                        await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await resampler.ReadFrameAsync());
                        break;
                    case 2:
                    case 3:
                        await Assert.ThrowsAsync<OperationCanceledException>(async () => await resampler.ReadFrameAsync(cancellation.Token));
                        break;
                }
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Equal(0, processing.AllocatedBytes);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await resampler.ReadFrameAsync());
            }
        }

        [Fact]
        public async Task EarlyDisposalReleasesReadAheadAndEmptySourcesHaveNoOutput()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioSignalSource source = new(allocator, 48000, 48000, (_, _) => 1, [997]);
            await using (AudioResampler resampler = new(source.ReadAsync, 400, allocator, new AudioResampleOptions { SamplesPerFrame = 1 }))
            {
                using AudioFrame frame = (await resampler.ReadFrameAsync())!;
                Assert.InRange(source.BlocksRead, 1, 3);
                await resampler.DisposeAsync();
                Assert.Equal(sizeof(double), allocator.AllocatedBytes);
                Assert.True(double.IsFinite(MemoryMarshal.Read<double>(frame.GetPlane(0).Span)));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            AudioSignalSource empty = new(allocator, 48000, 0, (_, _) => 0, [1]);
            await using AudioResampler noOutput = new(empty.ReadAsync, 400, allocator);
            Assert.Null(await noOutput.ReadFrameAsync());
            Assert.Null(await noOutput.ReadFrameAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task FormatTimeAndNonFiniteFailuresCannotReturnPartialOutput(int mode)
        {
            MediaBufferAllocator allocator = new(32768);
            AudioSignalSource source = new(allocator, 400, 10, (index, _) => mode == 2 && index == 2 ? double.NaN : 0.5, [1]);
            AudioSignalSource changed = new(allocator, 401, 10, (_, _) => 0.5, [1]);
            if (mode == 1)
            {
                source.Timestamp = position => position == 0 ? null : new MediaTimestamp(position, new TimeBase(1, 400));
            }
            if (mode == 3)
            {
                source.Timestamp = position => position == 0 ? new MediaTimestamp(0, new TimeBase(1, 400)) : null;
            }
            ValueTask<AudioFrame?> Read(CancellationToken token)
            {
                if (mode == 0 && source.BlocksRead != 0)
                {
                    return changed.ReadAsync(token);
                }
                return source.ReadAsync(token);
            }
            await using AudioResampler resampler = new(Read, 400, allocator);
            if (mode < 2)
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => await resampler.ReadFrameAsync());
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () => await resampler.ReadFrameAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await resampler.ReadFrameAsync());
        }

        [Fact]
        public async Task ConcurrentOperationsDoNotCorruptAnOutstandingRead()
        {
            MediaBufferAllocator allocator = new(32768);
            TaskCompletionSource<AudioFrame?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using AudioResampler resampler = new(_ => new ValueTask<AudioFrame?>(completion.Task), 400, allocator);
            ValueTask<AudioFrame?> pending = resampler.ReadFrameAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await resampler.ReadFrameAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await resampler.DisposeAsync());
            completion.SetResult(null);
            Assert.Null(await pending);
            Assert.Null(await resampler.ReadFrameAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task UnrepresentableExactClockAndInvalidOptionsFailExplicitly()
        {
            MediaBufferAllocator allocator = new(32768);
            AudioSignalSource source = new(allocator, 48000, 1, (_, _) => 0.5, [1]);
            source.Timestamp = position => new MediaTimestamp(position, new TimeBase(1, int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioResampler(source.ReadAsync, 0, allocator));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioResampler(source.ReadAsync, 400, allocator, new AudioResampleOptions { CutoffRatio = double.NaN }));
            await using AudioResampler resampler = new(source.ReadAsync, 400, allocator);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await resampler.ReadFrameAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static async Task<double[]> ConvertAsync(int inputBlock, int outputBlock)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioSignalSource source = new(allocator, 44100, 16387,
                (index, _) => index == 401 || index == 1000 ? 1 : 0.2 * Math.Sin(index * 0.007), [inputBlock]);
            List<double> result = [];
            await using AudioResampler resampler = new(source.ReadAsync, 400, allocator, new AudioResampleOptions { SamplesPerFrame = outputBlock });
            while (true)
            {
                using AudioFrame? frame = await resampler.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                result.AddRange(MemoryMarshal.Cast<byte, double>(frame.GetPlane(0).Span).ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            return result.ToArray();
        }
    }
}
