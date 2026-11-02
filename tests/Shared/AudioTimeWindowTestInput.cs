// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AudioTimeWindowTestInput
    {
        private const int Samples = 73;

        public static async Task VerifyAsync(AudioStorageLayout layout, int rate, MediaTimestamp origin)
        {
            foreach ((int start, int? end) in new (int, int?)[]
                { (-3, 6), (1, 37), (15, 89), (0, 200), (146, 148), (150, 200), (0, 0), (6, 6), (6, null), (0, null) })
            {
                await VerifyWindowAsync(layout, rate, origin, At(origin, start, 2 * rate),
                    end is int limit ? At(origin, limit, 2 * rate) : null);
            }
            await VerifyWindowAsync(layout, rate, origin, new(1, new(1, 1000003)), new(1, new(1, 999983)));
        }

        private static async Task VerifyWindowAsync(AudioStorageLayout layout, int rate, MediaTimestamp origin,
            MediaTimestamp start, MediaTimestamp? end)
        {
            MediaBufferAllocator allocator = new(65536);
            AudioSignalSource source = new(allocator, rate, Samples, (index, channel) => index + channel * 100,
                [3, 7, 5], layout, ["left", "right"])
            { Timestamp = index => At(origin, index, rate) };
            int[] selected = Enumerable.Range(0, Samples).Where(index =>
                CompareSample(origin, index, rate, start) >= 0 &&
                (end is null || CompareSample(origin, index, rate, end.Value) < 0)).ToArray();
            await using AudioTrimmer window = AudioTrimmer.CreateTimeWindow(source.ReadAsync, start, end, allocator);
            int position = 0;
            AudioFrame? retained = null;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await window.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    Require(position < selected.Length && frame.Timestamp == At(origin, selected[position], rate));
                    for (int index = 0; index < frame.SamplesPerChannel; index++)
                    {
                        Require(position < selected.Length);
                        for (int channel = 0; channel < 2; channel++)
                        {
                            ReadOnlySpan<double> samples = MemoryMarshal.Cast<byte, double>(frame.GetPlane(
                                layout == AudioStorageLayout.Planar ? channel : 0).Span);
                            int sample = layout == AudioStorageLayout.Planar ? index : 2 * index + channel;
                            Require(samples[sample] == selected[position] + channel * 100);
                        }
                        position++;
                    }
                    retained?.Dispose();
                    retained = frame.Retain();
                }
                Require(position == selected.Length);
                int reads = source.BlocksRead;
                Require(await window.ReadFrameAsync() is null && reads == source.BlocksRead);
                if (end == start)
                {
                    Require(reads == 0);
                }
                await window.DisposeAsync();
                if (retained is not null)
                {
                    Require(retained.GetPlane(0).Length > 0 && allocator.AllocatedBytes == retained.Format.GetBufferSize(retained.SamplesPerChannel));
                }
            }
            finally
            {
                retained?.Dispose();
            }
            Require(allocator.AllocatedBytes == 0);
        }

        public static MediaTimestamp At(MediaTimestamp origin, long numerator, int denominator)
        {
            int common = checked(origin.TimeBase.Denominator * (denominator / GreatestCommonDivisor(origin.TimeBase.Denominator, denominator)));
            return new(checked(origin.Value * origin.TimeBase.Numerator * (common / origin.TimeBase.Denominator)
                + numerator * (common / denominator)), new(1, common));
        }

        private static int CompareSample(MediaTimestamp origin, int sample, int rate, MediaTimestamp boundary)
        {
            BigInteger instant = (BigInteger)origin.Value * origin.TimeBase.Numerator * rate + (BigInteger)sample * origin.TimeBase.Denominator;
            BigInteger left = instant * boundary.TimeBase.Denominator;
            BigInteger right = (BigInteger)boundary.Value * boundary.TimeBase.Numerator * origin.TimeBase.Denominator * rate;
            return left.CompareTo(right);
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                (left, right) = (right, left % right);
            }
            return left;
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("The audio presentation window changed independently selected samples, exact timestamps or ownership.");
            }
        }
    }
}
