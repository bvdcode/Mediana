using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioLevelAccumulatorTests
    {
        [Fact]
        public Task RealWaveFileStreamsThroughTheFullWaveformPath() =>
            AudioWaveformTestInput.VerifyWaveAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Wave"));

        [Fact]
        public Task CompleteMixResampleQuantizeAndRmsPathPreservesToneEnergyAndPartialTail() => AudioWaveformTestInput.VerifyAsync();

        [Fact]
        public async Task WindowsSpanFramesAndIncludeSilenceAndThePartialTail()
        {
            MediaBufferAllocator allocator = new(4096);
            double[] values = [0, 0, 0, 1, -1, 0.5, 0.25];
            AudioSignalSource source = new(allocator, 400, values.Length, (index, _) => values[index], [2, 1]);
            AudioLevelAccumulator accumulator = new(3);
            List<AudioLevel> levels = [];
            while (true)
            {
                using AudioFrame? frame = await source.ReadAsync(default);
                if (frame is null)
                {
                    break;
                }
                accumulator.Process(frame, levels.Add);
            }
            accumulator.Complete(levels.Add);
            accumulator.Complete(levels.Add);
            Assert.Equal(3, levels.Count);
            Assert.Equal(new AudioLevel(0, 3, new MediaTimestamp(-7, new TimeBase(1, 400)), new MediaDuration(3, new TimeBase(1, 400)), 0, 0), levels[0]);
            Assert.Equal(Math.Sqrt(0.75), levels[1].RootMeanSquare, 14);
            Assert.Equal(1, levels[1].Peak);
            Assert.Equal(0.25, levels[2].RootMeanSquare);
            Assert.Equal(1, levels[2].SampleCount);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task UnknownWindowTimesRemainUnknownAndDiscontinuousFramesFail()
        {
            MediaBufferAllocator allocator = new(4096);
            AudioSignalSource source = new(allocator, 400, 2, (_, _) => 0.5, [1]);
            source.Timestamp = _ => null;
            AudioLevelAccumulator unknown = new(1);
            using (AudioFrame frame = (await source.ReadAsync(default))!)
            {
                unknown.Process(frame, level => Assert.Null(level.Timestamp));
            }
            source.Timestamp = position => new MediaTimestamp(position + 10, new TimeBase(1, 400));
            using AudioFrame second = (await source.ReadAsync(default))!;
            Assert.Throws<NotSupportedException>(() => unknown.Process(second, _ => { }));
            AudioSignalSource known = new(allocator, 400, 2, (_, _) => 0.5, [1]);
            AudioLevelAccumulator discontinuous = new(1);
            using (AudioFrame first = (await known.ReadAsync(default))!)
            {
                discontinuous.Process(first, _ => { });
            }
            known.Timestamp = position => new MediaTimestamp(position + 10, new TimeBase(1, 400));
            using AudioFrame wrongTime = (await known.ReadAsync(default))!;
            Assert.Throws<InvalidDataException>(() => discontinuous.Process(wrongTime, _ => { }));
        }

        [Theory]
        [InlineData(double.MaxValue)]
        [InlineData(1e-250)]
        public async Task ScaledEnergyDoesNotOverflowOrUnderflowForFiniteSamples(double amplitude)
        {
            MediaBufferAllocator allocator = new(4096);
            AudioSignalSource source = new(allocator, 400, 4, (_, _) => amplitude, [4]);
            using AudioFrame input = (await source.ReadAsync(default))!;
            AudioLevelAccumulator accumulator = new(4);
            AudioLevel? result = null;
            accumulator.Process(input, level => result = level);
            Assert.NotNull(result);
            Assert.Equal(amplitude, result.Value.RootMeanSquare);
            Assert.Equal(amplitude, result.Value.Peak);
        }

        [Fact]
        public async Task CancellationInvalidInputAndEmitterFailureMakeTheAccumulatorTerminal()
        {
            MediaBufferAllocator allocator = new(4096);
            AudioSignalSource source = new(allocator, 400, 4, (_, _) => 0.5, [4]);
            using AudioFrame input = (await source.ReadAsync(default))!;
            AudioLevelAccumulator cancelled = new(2);
            Assert.Throws<OperationCanceledException>(() => cancelled.Process(input, _ => { }, new CancellationToken(true)));
            Assert.Throws<InvalidOperationException>(() => cancelled.Complete(_ => { }));
            AudioLevelAccumulator failed = new(2);
            Assert.Throws<IOException>(() => failed.Process(input, _ => throw new IOException("Output failed.")));
            Assert.Throws<InvalidOperationException>(() => failed.Process(input, _ => { }));
            AudioLevelAccumulator complete = new(2);
            complete.Complete(_ => throw new InvalidOperationException());
            Assert.Throws<InvalidOperationException>(() => complete.Process(input, _ => { }));
            AudioSignalSource nonfinite = new(allocator, 400, 1, (_, _) => double.NaN, [1]);
            using AudioFrame nan = (await nonfinite.ReadAsync(default))!;
            AudioLevelAccumulator invalid = new(1);
            Assert.Throws<InvalidDataException>(() => invalid.Process(nan, _ => { }));
            AudioSignalSource stereo = new(allocator, 400, 1, (_, _) => 0.5, [1], channels: ["front-left", "front-right"]);
            using AudioFrame stereoFrame = (await stereo.ReadAsync(default))!;
            Assert.Throws<NotSupportedException>(() => new AudioLevelAccumulator(1).Process(stereoFrame, _ => { }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioLevelAccumulator(0));
        }
    }
}
