// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class Pcm16ConversionTests
    {
        [Fact]
        public void Float32MatchesScalarQuantizationAroundEveryPcm16HalfwayPoint()
        {
            List<float> values = [float.MinValue, float.MaxValue, -2, -1, -float.Epsilon, -0f, 0, float.Epsilon, 1, 2];
            for (int sample = short.MinValue; sample <= short.MaxValue; sample++)
            {
                float halfway = (sample + 0.5f) / 32768f;
                values.Add(float.BitDecrement(halfway));
                values.Add(halfway);
                values.Add(float.BitIncrement(halfway));
            }
            AssertFloatSamples(values.ToArray());
        }

        [Fact]
        public void Signed32MatchesScalarQuantizationAroundEveryPcm16HalfwayPoint()
        {
            List<int> values = [int.MinValue, int.MaxValue, -1, 0, 1];
            for (int sample = short.MinValue; sample <= short.MaxValue; sample++)
            {
                int halfway = checked(sample * 65536 + 32768);
                values.Add(halfway - 1);
                values.Add(halfway);
                values.Add(halfway + 1);
            }
            AssertIntegerSamples(values.ToArray());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(31)]
        [InlineData(32)]
        [InlineData(33)]
        public void VectorBlocksAndTailsPreserveSampleOrderAndTiming(int length)
        {
            foreach (int channels in new[] { 1, 2, 3 })
            {
                float[] floats = new float[length * channels];
                int[] integers = new int[floats.Length];
                Random random = new(417);
                for (int index = 0; index < floats.Length; index++)
                {
                    floats[index] = (float)(random.NextDouble() * 4 - 2);
                    integers[index] = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
                }
                AssertFloatSamples(floats, channels);
                AssertIntegerSamples(integers, channels);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(16)]
        public void NonFiniteVectorLanesAndTailFailWithoutLeakingOutput(int position)
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                float[] samples = new float[17];
                samples[position] = value;
                MediaBufferAllocator allocator = new(4096);
                using AudioFrame input = Frame(allocator, samples, AudioSampleFormat.Float32, 1);
                long baseline = allocator.AllocatedBytes;
                Assert.Throws<InvalidDataException>(() => AudioSampleConverter.Apply(input, AudioSampleFormat.Signed16,
                    AudioStorageLayout.Interleaved, allocator));
                Assert.Equal(baseline, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public void CancellationAndAllocationFailureDoNotLeakOutput()
        {
            MediaBufferAllocator allocator = new(4096);
            using AudioFrame input = Frame<float>(allocator, [0.5f, -0.25f], AudioSampleFormat.Float32, 1);
            long baseline = allocator.AllocatedBytes;
            Assert.Throws<OperationCanceledException>(() => AudioSampleConverter.Apply(input, AudioSampleFormat.Signed16,
                AudioStorageLayout.Interleaved, allocator, new CancellationToken(true)));
            Assert.Equal(baseline, allocator.AllocatedBytes);
            MediaBufferAllocator limited = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => AudioSampleConverter.Apply(input, AudioSampleFormat.Signed16,
                AudioStorageLayout.Interleaved, limited));
            Assert.Equal(0, limited.AllocatedBytes);
        }

        private static void AssertFloatSamples(float[] samples, int channels = 1)
        {
            short[] expected = samples.Select(value => (short)Math.Clamp(
                Math.Round(Math.Clamp((double)value, -1, 1) * 32768), short.MinValue, short.MaxValue)).ToArray();
            AssertSamples(samples, AudioSampleFormat.Float32, expected, channels);
        }

        private static void AssertIntegerSamples(int[] samples, int channels = 1)
        {
            short[] expected = samples.Select(value => (short)Math.Clamp(
                Math.Round(value / 65536d), short.MinValue, short.MaxValue)).ToArray();
            AssertSamples(samples, AudioSampleFormat.Signed32, expected, channels);
        }

        private static void AssertSamples<T>(T[] samples, AudioSampleFormat format, short[] expected, int channels) where T : unmanaged
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using AudioFrame input = Frame(allocator, samples, format, channels);
            using AudioFrame output = AudioSampleConverter.Apply(input, AudioSampleFormat.Signed16,
                AudioStorageLayout.Interleaved, allocator);
            Assert.Equal(input.Timestamp, output.Timestamp);
            Assert.Equal(input.Duration, output.Duration);
            Assert.Equal(input.Format.Channels, output.Format.Channels);
            Assert.Equal(input.SamplesPerChannel, output.SamplesPerChannel);
            input.Dispose();
            Assert.Equal(expected, MemoryMarshal.Cast<byte, short>(output.GetPlane(0).Span).ToArray());
            Assert.Equal(expected.Length * sizeof(short), allocator.AllocatedBytes);
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static AudioFrame Frame<T>(MediaBufferAllocator allocator, T[] samples, AudioSampleFormat sampleFormat, int channels) where T : unmanaged
        {
            string[] channelNames = Enumerable.Range(0, channels).Select(channel => $"channel-{channel}").ToArray();
            AudioFormat format = new(48000, sampleFormat, AudioStorageLayout.Interleaved, channelNames);
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(samples.AsSpan());
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(format, samples.Length / channels, buffer, new MediaTimestamp(-7, new TimeBase(1, 48000)));
        }
    }
}
