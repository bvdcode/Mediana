// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioStorageCopyTests
    {
        [Theory]
        [InlineData(AudioSampleFormat.Unsigned8, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Unsigned8, AudioStorageLayout.Planar)]
        [InlineData(AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Signed16, AudioStorageLayout.Planar)]
        [InlineData(AudioSampleFormat.Signed32, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Signed32, AudioStorageLayout.Planar)]
        public void MatchingIntegerStoragePreservesAllBytesAndTimingWithSeparateOwnership(AudioSampleFormat sampleFormat, AudioStorageLayout layout)
        {
            const int SamplesPerChannel = 8193;
            MediaBufferAllocator inputAllocator = new(256 * 1024);
            MediaBufferAllocator outputAllocator = new(256 * 1024);
            AudioFormat format = new(48000, sampleFormat, layout, ["left", "right", "center"]);
            byte[] expected = new byte[format.GetBufferSize(SamplesPerChannel)];
            new Random(417).NextBytes(expected);
            using AudioFrame input = CreateFrame(inputAllocator, format, SamplesPerChannel, expected);
            using AudioFrame output = AudioSampleConverter.Apply(input, sampleFormat, layout, outputAllocator);
            Assert.Equal(input.Timestamp, output.Timestamp);
            Assert.Equal(input.Duration, output.Duration);
            Assert.Equal(input.Format.Channels, output.Format.Channels);
            Assert.Equal(input.SamplesPerChannel, output.SamplesPerChannel);
            Assert.Equal(expected.Length, inputAllocator.AllocatedBytes);
            Assert.Equal(expected.Length, outputAllocator.AllocatedBytes);
            input.Dispose();
            Assert.Equal(0, inputAllocator.AllocatedBytes);
            Assert.Equal(expected.Length, outputAllocator.AllocatedBytes);
            int planeBytes = expected.Length / format.PlaneCount;
            for (int plane = 0; plane < format.PlaneCount; plane++)
            {
                Assert.Equal(expected.AsSpan(plane * planeBytes, planeBytes).ToArray(), output.GetPlane(plane).ToArray());
            }
            output.Dispose();
            Assert.Equal(0, outputAllocator.AllocatedBytes);
        }

        [Fact]
        public void MatchingStorageStillHonorsCancellationAndDestinationBudget()
        {
            MediaBufferAllocator allocator = new(4096);
            AudioFormat format = new(48000, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, ["mono"]);
            using AudioFrame input = CreateFrame(allocator, format, 4, [0, 0, 255, 127, 0, 128, 255, 255]);
            MediaBufferAllocator limited = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => AudioSampleConverter.Apply(input, format.SampleFormat, format.Layout, limited));
            Assert.Equal(0, limited.AllocatedBytes);
            long baseline = allocator.AllocatedBytes;
            Assert.Throws<OperationCanceledException>(() => AudioSampleConverter.Apply(input, format.SampleFormat, format.Layout,
                allocator, new CancellationToken(true)));
            Assert.Equal(baseline, allocator.AllocatedBytes);
            Assert.Equal(new byte[] { 0, 0, 255, 127, 0, 128, 255, 255 }, input.GetPlane(0).ToArray());
        }

        [Theory]
        [InlineData(AudioSampleFormat.Float32)]
        [InlineData(AudioSampleFormat.Float64)]
        public void MatchingFloatingPointStorageStillRejectsNonFiniteSamples(AudioSampleFormat sampleFormat)
        {
            MediaBufferAllocator allocator = new(4096);
            AudioFormat format = new(48000, sampleFormat, AudioStorageLayout.Interleaved, ["mono"]);
            byte[] bytes = sampleFormat switch
            {
                AudioSampleFormat.Float32 => BitConverter.GetBytes(float.NaN),
                AudioSampleFormat.Float64 => BitConverter.GetBytes(double.NaN),
                _ => throw new ArgumentOutOfRangeException(nameof(sampleFormat))
            };
            using AudioFrame input = CreateFrame(allocator, format, 1, bytes);
            long baseline = allocator.AllocatedBytes;
            Assert.Throws<InvalidDataException>(() => AudioSampleConverter.Apply(input, sampleFormat, format.Layout, allocator));
            Assert.Equal(baseline, allocator.AllocatedBytes);
        }

        private static AudioFrame CreateFrame(MediaBufferAllocator allocator, AudioFormat format, int samplesPerChannel, byte[] bytes)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(format, samplesPerChannel, buffer, new MediaTimestamp(-7, new TimeBase(1, 48000)));
        }
    }
}
