// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioBoundaryTests
    {
        [Fact]
        public void ReflectionHasNoRepeatedEndpointsAndHandlesManyPeriodsWithoutOverflow()
        {
            long[] expected = [2, 1, 0, 1, 2, 3, 2, 1, 0, 1, 2, 3, 2, 1];
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.Equal(expected[index], AudioBoundaryExtension.Resolve(index - 2, 4, AudioBoundaryMode.Reflect));
            }
            Assert.Equal(0, AudioBoundaryExtension.Resolve(long.MinValue, 1, AudioBoundaryMode.Reflect));
            Assert.Equal(0, AudioBoundaryExtension.Resolve(long.MaxValue, 1, AudioBoundaryMode.Reflect));
            Assert.Equal(long.MaxValue - 2, AudioBoundaryExtension.Resolve(long.MaxValue, long.MaxValue, AudioBoundaryMode.Reflect));
            Assert.Equal(long.MaxValue - 3, AudioBoundaryExtension.Resolve(long.MinValue, long.MaxValue, AudioBoundaryMode.Reflect));
            Assert.Equal(-1, AudioBoundaryExtension.Resolve(-1, 4, AudioBoundaryMode.Zero));
            Assert.Equal(-1, AudioBoundaryExtension.Resolve(4, 4, AudioBoundaryMode.Zero));
        }

        [Theory]
        [InlineData(1, 48000, 400)]
        [InlineData(7, 48000, 400)]
        [InlineData(17, 8000, 11025)]
        [InlineData(4001, 44100, 400)]
        public async Task ConstantAmplitudeSurvivesBothEdgesAndInputsShorterThanTheFilter(int count, int rate, int outputRate)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioSignalSource source = new(allocator, rate, count, (_, channel) => channel switch
            {
                0 => 0.25,
                1 => -0.75,
                _ => throw new ArgumentOutOfRangeException(nameof(channel))
            }, [1, 3, 17], AudioStorageLayout.Planar, ["left", "right"]);
            await using AudioResampler resampler = new(source.ReadAsync, outputRate, allocator,
                new AudioResampleOptions { BoundaryMode = AudioBoundaryMode.Reflect, SamplesPerFrame = 7 });
            long position = 0;
            while (true)
            {
                using AudioFrame? frame = await resampler.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                ReadOnlySpan<double> values = MemoryMarshal.Cast<byte, double>(frame.GetPlane(0).Span);
                for (int sample = 0; sample < frame.SamplesPerChannel; sample++)
                {
                    Assert.InRange(Math.Abs(values[sample * 2] - 0.25), 0, 2e-12);
                    Assert.InRange(Math.Abs(values[sample * 2 + 1] + 0.75), 0, 2e-12);
                }
                position += frame.SamplesPerChannel;
            }
            Assert.Equal(((long)count * outputRate + rate - 1) / rate, position);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(9, 48000, 800)]
        [InlineData(8001, 44100, 400)]
        [InlineData(101, 8000, 11025)]
        public async Task ReflectionDoesNotDependOnPacketOrOutputBoundaries(int count, int rate, int outputRate)
        {
            double[] first = await ConvertAsync(count, rate, outputRate, 1, 7);
            double[] second = await ConvertAsync(count, rate, outputRate, 103, 73);
            Assert.Equal(first, second);
        }

        [Fact]
        public void InvalidBoundaryModesAreRejectedBeforeReadingInput()
        {
            MediaBufferAllocator allocator = new(1024);
            AudioSignalSource source = new(allocator, 8000, 10, (_, _) => 1, [10]);
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioResampler(source.ReadAsync, 400, allocator,
                new AudioResampleOptions { BoundaryMode = (AudioBoundaryMode)7 }));
            Assert.Equal(0, source.BlocksRead);
        }

        private static async Task<double[]> ConvertAsync(int count, int rate, int outputRate, int block, int outputBlock)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioSignalSource source = new(allocator, rate, count, (index, _) => index switch
            {
                0 => 0.5,
                _ when index == count - 1 => -0.75,
                _ => 0.2 * Math.Sin(2 * Math.PI * 97 * index / rate)
            }, [block]);
            await using AudioResampler resampler = new(source.ReadAsync, outputRate, allocator,
                new AudioResampleOptions { BoundaryMode = AudioBoundaryMode.Reflect, SamplesPerFrame = outputBlock });
            List<double> output = [];
            while (true)
            {
                using AudioFrame? frame = await resampler.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                output.AddRange(MemoryMarshal.Cast<byte, double>(frame.GetPlane(0).Span).ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            return output.ToArray();
        }
    }
}
