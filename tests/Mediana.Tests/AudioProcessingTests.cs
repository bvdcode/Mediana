using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioProcessingTests
    {
        [Theory]
        [InlineData(AudioSampleFormat.Unsigned8)]
        [InlineData(AudioSampleFormat.Signed16)]
        [InlineData(AudioSampleFormat.Signed32)]
        [InlineData(AudioSampleFormat.Float32)]
        [InlineData(AudioSampleFormat.Float64)]
        public void SampleRepresentationsHaveKnownScaleAndPreserveTiming(AudioSampleFormat format)
        {
            MediaBufferAllocator allocator = new(4096);
            using AudioFrame input = Doubles(allocator, [-1, -0.5, 0, 0.5, 1]);
            using AudioFrame encoded = AudioSampleConverter.Apply(input, format, AudioStorageLayout.Interleaved, allocator);
            byte[] expected = format switch
            {
                AudioSampleFormat.Unsigned8 => [0, 64, 128, 192, 255],
                AudioSampleFormat.Signed16 => MemoryMarshal.AsBytes<short>(new short[] { short.MinValue, -16384, 0, 16384, short.MaxValue }).ToArray(),
                AudioSampleFormat.Signed32 => MemoryMarshal.AsBytes<int>(new[] { int.MinValue, -1073741824, 0, 1073741824, int.MaxValue }).ToArray(),
                AudioSampleFormat.Float32 => MemoryMarshal.AsBytes<float>(new float[] { -1, -0.5f, 0, 0.5f, 1 }).ToArray(),
                AudioSampleFormat.Float64 => MemoryMarshal.AsBytes<double>(new double[] { -1, -0.5, 0, 0.5, 1 }).ToArray(),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            Assert.Equal(expected, encoded.GetPlane(0).ToArray());
            using AudioFrame decoded = AudioSampleConverter.Apply(encoded, AudioSampleFormat.Float64, AudioStorageLayout.Planar, allocator);
            double maximum = format switch
            {
                AudioSampleFormat.Unsigned8 => 127 / 128d,
                AudioSampleFormat.Signed16 => 32767 / 32768d,
                AudioSampleFormat.Signed32 => int.MaxValue / 2147483648d,
                AudioSampleFormat.Float32 or AudioSampleFormat.Float64 => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            Assert.Equal(new[] { -1, -0.5, 0, 0.5, maximum }, MemoryMarshal.Cast<byte, double>(decoded.GetPlane(0).Span).ToArray());
            Assert.Equal(input.Timestamp, decoded.Timestamp);
            Assert.Equal(input.Duration, decoded.Duration);
        }

        [Fact]
        public void QuantizationSaturatesAndUsesUnbiasedTieRounding()
        {
            MediaBufferAllocator allocator = new(4096);
            using AudioFrame input = Doubles(allocator, [-2, -1, -0.5, -1.5 / 32768, -0.5 / 32768, 0.5 / 32768, 1.5 / 32768, 0.5, 1, 2]);
            using AudioFrame output = AudioSampleConverter.Apply(input, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator);
            Assert.Equal(new short[] { short.MinValue, short.MinValue, -16384, -2, 0, 0, 2, 16384, short.MaxValue, short.MaxValue },
                MemoryMarshal.Cast<byte, short>(output.GetPlane(0).Span).ToArray());
        }

        [Theory]
        [InlineData(AudioStorageLayout.Interleaved)]
        [InlineData(AudioStorageLayout.Planar)]
        public void ExplicitMatrixUsesChannelOrderAndPreservesHeadroom(AudioStorageLayout layout)
        {
            MediaBufferAllocator allocator = new(4096);
            double[] data = layout switch
            {
                AudioStorageLayout.Interleaved => [1, 10, 100, 2, 20, 200],
                AudioStorageLayout.Planar => [1, 2, 10, 20, 100, 200],
                _ => throw new ArgumentOutOfRangeException(nameof(layout))
            };
            using AudioFrame input = Doubles(allocator, data, layout, ["left", "right", "center"]);
            double[] coefficients = [0, 1, 0, 1, 0, 0.5];
            string[] channels = ["selected-right", "left-plus-center"];
            AudioChannelMixer mixer = new(input.Format, channels, coefficients);
            coefficients[0] = 999;
            channels[0] = "changed";
            using AudioFrame output = mixer.Apply(input, allocator);
            Assert.Equal(new[] { 10d, 51, 20, 102 }, MemoryMarshal.Cast<byte, double>(output.GetPlane(0).Span).ToArray());
            Assert.Equal(new[] { "selected-right", "left-plus-center" }, output.Format.Channels);
            Assert.Equal(input.Timestamp, output.Timestamp);
            Assert.Equal(input.Duration, output.Duration);
            using AudioFrame planar = AudioSampleConverter.Apply(output, AudioSampleFormat.Float64, AudioStorageLayout.Planar, allocator);
            Assert.Equal(new[] { 10d, 20 }, MemoryMarshal.Cast<byte, double>(planar.GetPlane(0).Span).ToArray());
            Assert.Equal(new[] { 51d, 102 }, MemoryMarshal.Cast<byte, double>(planar.GetPlane(1).Span).ToArray());
        }

        [Fact]
        public void AntiPhaseStereoCancelsWithoutImplicitGainChanges()
        {
            MediaBufferAllocator allocator = new(4096);
            using AudioFrame input = Doubles(allocator, [0.25, -0.25, 1, -1, -0.5, 0.5], AudioStorageLayout.Interleaved, ["front-left", "front-right"]);
            AudioChannelMixer mixer = new(input.Format, ["mono"], [0.5, 0.5]);
            using AudioFrame output = mixer.Apply(input, allocator);
            Assert.Equal(new double[3], MemoryMarshal.Cast<byte, double>(output.GetPlane(0).Span).ToArray());
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFiniteInputsFailAndReleasePartialOutputs(double value)
        {
            MediaBufferAllocator allocator = new(4096);
            using AudioFrame input = Doubles(allocator, [0.25, value]);
            long baseline = allocator.AllocatedBytes;
            Assert.Throws<InvalidDataException>(() => AudioSampleConverter.Apply(input, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator));
            Assert.Equal(baseline, allocator.AllocatedBytes);
            AudioChannelMixer mixer = new(input.Format, ["mono"], [1]);
            Assert.Throws<InvalidDataException>(() => mixer.Apply(input, allocator));
            Assert.Equal(baseline, allocator.AllocatedBytes);
        }

        [Fact]
        public void InvalidMatrixFormatCancellationAndAllocationFailureAreExplicit()
        {
            MediaBufferAllocator allocator = new(4096);
            using AudioFrame input = Doubles(allocator, [0.5]);
            Assert.Throws<ArgumentException>(() => new AudioChannelMixer(input.Format, ["mono"], []));
            Assert.Throws<ArgumentException>(() => new AudioChannelMixer(input.Format, ["mono"], [double.NaN]));
            AudioChannelMixer mixer = new(input.Format, ["mono"], [1]);
            using AudioFrame stereo = Doubles(allocator, [0.5, 0.25], AudioStorageLayout.Interleaved, ["front-left", "front-right"]);
            Assert.Throws<NotSupportedException>(() => mixer.Apply(stereo, allocator));
            long baseline = allocator.AllocatedBytes;
            Assert.Throws<OperationCanceledException>(() => mixer.Apply(input, allocator, new CancellationToken(true)));
            Assert.Throws<OperationCanceledException>(() => AudioSampleConverter.Apply(input, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator, new CancellationToken(true)));
            Assert.Equal(baseline, allocator.AllocatedBytes);
            MediaBufferAllocator limited = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => mixer.Apply(input, limited));
            Assert.Equal(0, limited.AllocatedBytes);
            using AudioFrame overflow = Doubles(allocator, [double.MaxValue]);
            Assert.Throws<InvalidDataException>(() => AudioSampleConverter.Apply(overflow, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator));
        }

        [Fact]
        public void ConvertedOutputHasIndependentOwnership()
        {
            MediaBufferAllocator allocator = new(4096);
            AudioFrame input = Doubles(allocator, [0.5, -0.25]);
            AudioFrame output = AudioSampleConverter.Apply(input, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator);
            input.Dispose();
            Assert.Equal(new short[] { 16384, -8192 }, MemoryMarshal.Cast<byte, short>(output.GetPlane(0).Span).ToArray());
            Assert.Equal(4, allocator.AllocatedBytes);
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static AudioFrame Doubles(MediaBufferAllocator allocator, double[] samples,
            AudioStorageLayout layout = AudioStorageLayout.Interleaved, string[]? channels = null)
        {
            AudioFormat format = new(48000, AudioSampleFormat.Float64, layout, channels ?? ["mono"]);
            using WritableMediaBuffer writable = allocator.Allocate(samples.Length * sizeof(double));
            MemoryMarshal.AsBytes(samples.AsSpan()).CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(format, samples.Length / format.Channels.Count, buffer, new MediaTimestamp(-7, new TimeBase(1, 48000)));
        }
    }
}
