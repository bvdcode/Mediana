// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioSamplePreparationTests
    {
        [Theory]
        [InlineData(AudioSampleFormat.Unsigned8, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Unsigned8, AudioStorageLayout.Planar)]
        [InlineData(AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Signed16, AudioStorageLayout.Planar)]
        [InlineData(AudioSampleFormat.Signed32, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Signed32, AudioStorageLayout.Planar)]
        [InlineData(AudioSampleFormat.Float32, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Float32, AudioStorageLayout.Planar)]
        [InlineData(AudioSampleFormat.Float64, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Float64, AudioStorageLayout.Planar)]
        public void MatchingFormatRetainsStorageAndOriginalBudget(AudioSampleFormat sampleFormat, AudioStorageLayout layout)
            => AudioSamplePreparationTestInput.Verify(sampleFormat, layout);

        [Theory]
        [InlineData(AudioSampleFormat.Float32, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Float32, AudioStorageLayout.Planar)]
        [InlineData(AudioSampleFormat.Float64, AudioStorageLayout.Interleaved)]
        [InlineData(AudioSampleFormat.Float64, AudioStorageLayout.Planar)]
        public void MatchingFloatRejectsNonFiniteValuesInEveryPlane(AudioSampleFormat sampleFormat, AudioStorageLayout layout)
        {
            const int Samples = 513;
            AudioFormat format = new(48000, sampleFormat, layout, "left", "right");
            int bytes = format.GetBufferSize(Samples);
            MediaBufferAllocator source = new(bytes);
            MediaBufferAllocator destination = new(1);
            foreach (double value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                for (int plane = 0; plane < format.PlaneCount; plane++)
                {
                    using (WritableMediaBuffer writable = source.Allocate(bytes))
                    {
                        int end = (plane + 1) * bytes / format.PlaneCount;
                        switch (sampleFormat)
                        {
                            case AudioSampleFormat.Float32:
                                MemoryMarshal.Cast<byte, float>(writable.Memory.Span[..end])[^1] = (float)value;
                                break;
                            case AudioSampleFormat.Float64:
                                MemoryMarshal.Cast<byte, double>(writable.Memory.Span[..end])[^1] = value;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(sampleFormat));
                        }
                        using MediaBuffer buffer = writable.Publish();
                        using AudioFrame input = new(format, Samples, buffer);
                        Assert.Throws<InvalidDataException>(() => AudioSampleConverter.ConvertOrRetain(input, sampleFormat, layout, destination));
                        Assert.Equal(bytes, source.AllocatedBytes);
                        Assert.Equal(0, destination.AllocatedBytes);
                    }
                    Assert.Equal(0, source.AllocatedBytes);
                }
            }
        }

        [Theory]
        [InlineData(AudioStorageLayout.Interleaved)]
        [InlineData(AudioStorageLayout.Planar)]
        public void ChangedRepresentationCopiesIntoDestinationBudget(AudioStorageLayout layout)
        {
            AudioFormat format = new(44100, AudioSampleFormat.Signed16, layout, "right", "left");
            MediaBufferAllocator source = new(4096);
            MediaBufferAllocator destination = new(4096);
            using AudioFrame input = AudioSamplePreparationTestInput.CreateFrame(format, 83, source);
            using AudioFrame expected = AudioSampleConverter.Apply(input, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, destination);
            using AudioFrame output = AudioSampleConverter.ConvertOrRetain(input, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, destination);
            Assert.Equal(expected.GetPlane(0).ToArray(), output.GetPlane(0).ToArray());
            Assert.False(input.GetPlane(0).Equals(output.GetPlane(0)));
            Assert.Equal(input.Timestamp, output.Timestamp);
            Assert.Equal(input.Duration, output.Duration);
            Assert.Equal(format.Channels, output.Format.Channels);
            input.Dispose();
            Assert.Equal(0, source.AllocatedBytes);
            Assert.Equal(2 * output.GetPlane(0).Length, destination.AllocatedBytes);
            expected.Dispose();
            output.Dispose();
            Assert.Equal(0, destination.AllocatedBytes);
        }

        [Fact]
        public void CancellationInvalidFormatsAndDisposedInputDoNotAcquireStorage()
        {
            AudioFormat format = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "mono");
            MediaBufferAllocator source = new(4096);
            MediaBufferAllocator destination = new(4096);
            using AudioFrame input = AudioSamplePreparationTestInput.CreateFrame(format, 257, source);
            Assert.Throws<OperationCanceledException>(() => AudioSampleConverter.ConvertOrRetain(input, format.SampleFormat,
                format.Layout, destination, new CancellationToken(true)));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioSampleConverter.ConvertOrRetain(input, (AudioSampleFormat)int.MaxValue,
                format.Layout, destination));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioSampleConverter.ConvertOrRetain(input, format.SampleFormat,
                (AudioStorageLayout)int.MaxValue, destination));
            Assert.Equal(format.GetBufferSize(input.SamplesPerChannel), source.AllocatedBytes);
            Assert.Equal(0, destination.AllocatedBytes);
            input.Dispose();
            Assert.Throws<ObjectDisposedException>(() => AudioSampleConverter.ConvertOrRetain(input, format.SampleFormat, format.Layout, destination));
            Assert.Equal(0, source.AllocatedBytes);
            Assert.Equal(0, destination.AllocatedBytes);
        }
    }
}
