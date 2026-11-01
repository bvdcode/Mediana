// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AudioSamplePreparationTestInput
    {
        public static void Verify(AudioSampleFormat sampleFormat, AudioStorageLayout layout)
        {
            const int SamplesPerChannel = 8193;
            const int Repetitions = 8;
            AudioFormat format = new(48000, sampleFormat, layout, "right", "left", "center");
            int bytes = format.GetBufferSize(SamplesPerChannel);
            MediaBufferAllocator source = new(bytes);
            MediaBufferAllocator destination = new(1);
            using AudioFrame input = CreateFrame(format, SamplesPerChannel, source);
            for (int warmup = 0; warmup < 3; warmup++)
            {
                using AudioFrame prepared = AudioSampleConverter.ConvertOrRetain(input, sampleFormat, layout, destination);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < Repetitions; iteration++)
            {
                using AudioFrame prepared = AudioSampleConverter.ConvertOrRetain(input, sampleFormat, layout, destination);
                Require(ReferenceEquals(input.Format, prepared.Format));
                Require(input.Timestamp == prepared.Timestamp && input.Duration == prepared.Duration);
                Require(input.SamplesPerChannel == prepared.SamplesPerChannel);
                for (int plane = 0; plane < format.PlaneCount; plane++)
                {
                    Require(input.GetPlane(plane).Equals(prepared.GetPlane(plane)));
                }
                Require(source.AllocatedBytes == bytes && destination.AllocatedBytes == 0);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(allocated < (long)Repetitions * bytes / 2);
            using AudioFrame output = AudioSampleConverter.ConvertOrRetain(input, sampleFormat, layout, destination);
            using AudioFrame retained = output.Retain();
            input.Dispose();
            output.Dispose();
            Require(source.AllocatedBytes == bytes && destination.AllocatedBytes == 0);
            Require(retained.GetPlane(format.PlaneCount - 1).Length == bytes / format.PlaneCount);
            retained.Dispose();
            Require(source.AllocatedBytes == 0);
        }

        public static AudioFrame CreateFrame(AudioFormat format, int samples, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(samples));
            switch (format.SampleFormat)
            {
                case AudioSampleFormat.Unsigned8:
                case AudioSampleFormat.Signed16:
                case AudioSampleFormat.Signed32:
                    for (int index = 0; index < writable.Memory.Length; index++)
                    {
                        writable.Memory.Span[index] = (byte)(index * 37);
                    }
                    break;
                case AudioSampleFormat.Float32:
                    ReadOnlySpan<float> singles = [float.MinValue, -2.5f, -float.Epsilon, -0f, 0f, float.Epsilon, 1.5f, float.MaxValue];
                    Span<float> singleSamples = MemoryMarshal.Cast<byte, float>(writable.Memory.Span);
                    for (int index = 0; index < singleSamples.Length; index++)
                    {
                        singleSamples[index] = singles[index % singles.Length];
                    }
                    break;
                case AudioSampleFormat.Float64:
                    ReadOnlySpan<double> doubles = [double.MinValue, -2.5d, -double.Epsilon, -0d, 0d, double.Epsilon, 1.5d, double.MaxValue];
                    Span<double> doubleSamples = MemoryMarshal.Cast<byte, double>(writable.Memory.Span);
                    for (int index = 0; index < doubleSamples.Length; index++)
                    {
                        doubleSamples[index] = doubles[index % doubles.Length];
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(format, samples, buffer, new MediaTimestamp(-7, new TimeBase(1, format.SampleRate)));
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("PCM preparation changed storage, timing, allocation bounds or retained ownership.");
            }
        }
    }
}
