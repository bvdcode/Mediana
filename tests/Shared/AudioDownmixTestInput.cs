// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AudioDownmixTestInput
    {
        public static void Verify(bool stereo, AudioStorageLayout layout, AudioSampleFormat sampleFormat = AudioSampleFormat.Float64, bool sideSurround = false)
        {
            string[] speakers = ["low-frequency", "back-right", "front-center", "front-left", "back-left", "front-right"];
            if (sideSurround)
            {
                speakers[1] = "side-right";
                speakers[4] = "side-left";
            }
            double[] values = new double[8 * speakers.Length];
            for (int sample = 0; sample < 6; sample++)
            {
                Set(sample, sample, 1);
            }
            for (int channel = 0; channel < speakers.Length; channel++)
            {
                Set(6, channel, 1);
                Set(7, channel, -1);
            }
            MediaBufferAllocator allocator = new(8192);
            using (WritableMediaBuffer writable = allocator.Allocate(values.Length * sizeof(double)))
            {
                MemoryMarshal.AsBytes<double>(values).CopyTo(writable.Memory.Span);
                using MediaBuffer buffer = writable.Publish();
                using AudioFrame input = new(new(48000, AudioSampleFormat.Float64, layout, speakers), 8, buffer,
                    new MediaTimestamp(-73, new TimeBase(1, 96000)));
                AudioChannelMixer mixer = stereo ? AudioDownmix.CreateStereo(input.Format, sampleFormat) : AudioDownmix.CreateMono(input.Format, sampleFormat);
                using AudioFrame output = mixer.Apply(input, allocator);
                double rootHalf = Math.Sqrt(0.5);
                double divisor = stereo ? 1 + 2 * rootHalf : 2 + 2 * rootHalf;
                double[] expected = stereo
                    ? [0, 0, 0, rootHalf, rootHalf, rootHalf, 1, 0, rootHalf, 0, 0, 1]
                    : [0, 0.5, 1, rootHalf, 0.5, rootHalf];
                double[] actual = sampleFormat switch
                {
                    AudioSampleFormat.Float32 => MemoryMarshal.Cast<byte, float>(output.GetPlane(0).Span).ToArray().Select(value => (double)value).ToArray(),
                    AudioSampleFormat.Float64 => MemoryMarshal.Cast<byte, double>(output.GetPlane(0).Span).ToArray(),
                    _ => throw new ArgumentOutOfRangeException(nameof(sampleFormat))
                };
                double tolerance = sampleFormat switch
                {
                    AudioSampleFormat.Float32 => 2e-7,
                    AudioSampleFormat.Float64 => 2e-15,
                    _ => throw new ArgumentOutOfRangeException(nameof(sampleFormat))
                };
                for (int index = 0; index < expected.Length; index++)
                {
                    Require(Math.Abs(actual[index] - expected[index] / divisor) <= tolerance);
                }
                int channels = stereo ? 2 : 1;
                for (int channel = 0; channel < channels; channel++)
                {
                    Require(Math.Abs(actual[6 * channels + channel] - 1) <= tolerance);
                    Require(Math.Abs(actual[7 * channels + channel] + 1) <= tolerance);
                }
                Require(output.Timestamp == input.Timestamp && output.Duration == input.Duration);
                Require(output.Format.SampleFormat == sampleFormat);
                Require(output.Format.SampleRate == 48000 && output.Format.Layout == AudioStorageLayout.Interleaved);
                Require(output.Format.Channels.SequenceEqual(stereo ? ["front-left", "front-right"] : ["front-center"]));
            }
            Require(allocator.AllocatedBytes == 0);

            void Set(int sample, int channel, double value)
            {
                int index = layout switch
                {
                    AudioStorageLayout.Interleaved => sample * speakers.Length + channel,
                    AudioStorageLayout.Planar => channel * 8 + sample,
                    _ => throw new ArgumentOutOfRangeException(nameof(layout))
                };
                values[index] = value;
            }
        }

        public static void VerifyBalance()
        {
            MediaBufferAllocator allocator = new(1024);
            using (WritableMediaBuffer writable = allocator.Allocate(2 * sizeof(double)))
            {
                MemoryMarshal.Cast<byte, double>(writable.Memory.Span).Fill(1);
                using MediaBuffer buffer = writable.Publish();
                using AudioFrame input = new(new(48000, AudioSampleFormat.Float64, AudioStorageLayout.Interleaved,
                    "front-left", "front-center"), 1, buffer);
                using AudioFrame output = AudioDownmix.CreateStereo(input.Format).Apply(input, allocator);
                ReadOnlySpan<double> actual = MemoryMarshal.Cast<byte, double>(output.GetPlane(0).Span);
                Require(Math.Abs(actual[0] - 1) < 2e-15);
                Require(Math.Abs(actual[1] - (Math.Sqrt(2) - 1)) < 2e-15);
            }
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("The normalized downmix changed speaker contribution, headroom, timing or ownership.");
            }
        }
    }
}
