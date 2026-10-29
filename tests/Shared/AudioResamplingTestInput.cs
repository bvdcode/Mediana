// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AudioResamplingTestInput
    {
        public static async Task VerifyAsync(int inputRate, int outputRate, AudioStorageLayout layout)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            long samples = inputRate + 17;
            AudioSignalSource source = new(allocator, inputRate, samples,
                (index, channel) => Signal((double)index / inputRate, channel), [1, 17, 223], layout, ["front-left", "front-right"]);
            await using AudioResampler resampler = new(source.ReadAsync, outputRate, allocator,
                new AudioResampleOptions { SamplesPerFrame = 73 });
            long expectedCount = (samples * outputRate + inputRate - 1) / inputRate;
            int denominator = checked(inputRate / (int)System.Numerics.BigInteger.GreatestCommonDivisor(inputRate, outputRate) * outputRate);
            long position = 0;
            double maximumError = 0;
            AudioFrame? retained = null;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await resampler.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    if (frame.Format.SampleRate != outputRate || frame.Format.SampleFormat != AudioSampleFormat.Float64
                        || frame.Format.Layout != AudioStorageLayout.Interleaved
                        || !frame.Format.Channels.SequenceEqual(source.Format.Channels) || frame.SamplesPerChannel > 73
                        || frame.Timestamp != new MediaTimestamp(-7L * (denominator / inputRate) + position * (denominator / outputRate), new TimeBase(1, denominator))
                        || frame.Duration != new MediaDuration(frame.SamplesPerChannel, new TimeBase(1, outputRate)))
                    {
                        throw new InvalidDataException("Resampled PCM format, sample count or timing is incorrect.");
                    }
                    ReadOnlySpan<double> actual = MemoryMarshal.Cast<byte, double>(frame.GetPlane(0).Span);
                    for (int index = 0; index < frame.SamplesPerChannel; index++)
                    {
                        double seconds = (double)(position + index) / outputRate;
                        if (seconds > 0.1 && seconds < 0.9)
                        {
                            for (int channel = 0; channel < 2; channel++)
                            {
                                maximumError = Math.Max(maximumError, Math.Abs(actual[index * 2 + channel] - Signal(seconds, channel)));
                            }
                        }
                    }
                    position += frame.SamplesPerChannel;
                    retained?.Dispose();
                    retained = frame.Retain();
                }
                if (position != expectedCount || maximumError > 0.00005 || await resampler.ReadFrameAsync() is not null)
                {
                    throw new InvalidDataException($"Resampling failed: {position}/{expectedCount} samples, maximum passband error {maximumError}.");
                }
                await resampler.DisposeAsync();
                if (retained is null || allocator.AllocatedBytes != 73 * 2 * sizeof(double)
                    || retained.GetPlane(0).Length != retained.Format.GetBufferSize(retained.SamplesPerChannel))
                {
                    throw new InvalidOperationException("Resampling retained history or invalidated returned audio storage.");
                }
            }
            finally
            {
                retained?.Dispose();
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Resampling did not release its PCM storage.");
            }
        }

        private static double Signal(double seconds, int channel) => channel switch
        {
            0 => 0.5 * Math.Sin(2 * Math.PI * 50 * seconds),
            1 => 0.25 * Math.Cos(2 * Math.PI * 90 * seconds),
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }
}
