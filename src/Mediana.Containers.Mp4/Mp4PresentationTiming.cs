// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Containers.Mp4
{
    internal class Mp4PresentationTiming : IDisposable
    {
        private readonly MediaBuffer? corrections;

        private Mp4PresentationTiming(MediaBuffer? corrections) => this.corrections = corrections?.Retain();

        public static Mp4PresentationTiming Create(Mp4SampleTable table, MediaBufferAllocator allocator,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = table.SampleCount;
            using WritableMediaBuffer scratch = allocator.Allocate(checked(count * (sizeof(long) + sizeof(int))));
            using WritableMediaBuffer output = allocator.Allocate(checked(count * sizeof(long)));
            Span<long> timestamps = MemoryMarshal.Cast<byte, long>(scratch.Memory.Span[..(count * sizeof(long))]);
            Span<int> indices = MemoryMarshal.Cast<byte, int>(scratch.Memory.Span[(count * sizeof(long))..]);
            Span<long> durations = MemoryMarshal.Cast<byte, long>(output.Memory.Span);
            Mp4TrackCursor cursor = new(table);
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Mp4SampleLocation sample = cursor.Current;
                timestamps[index] = sample.PresentationTime;
                indices[index] = index;
                durations[index] = sample.Duration;
                cursor.Advance(sample);
            }
            timestamps.Sort(indices);
            cancellationToken.ThrowIfCancellationRequested();
            bool changed = false;
            int position = 0;
            while (position < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int after = position + 1;
                while (after < count && timestamps[after] == timestamps[position])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    after++;
                }
                if (after - position > 1)
                {
                    for (int duplicate = position; duplicate < after; duplicate++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        durations[indices[duplicate]] = -1;
                    }
                    changed = true;
                }
                else if (after < count)
                {
                    long duration = checked(timestamps[after] - timestamps[position]);
                    changed |= durations[indices[position]] != duration;
                    durations[indices[position]] = duration;
                }
                position = after;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!changed)
            {
                return new(null);
            }
            using MediaBuffer buffer = output.Publish();
            return new(buffer);
        }

        public long? GetDuration(int sampleIndex, uint decodeDuration)
        {
            if (corrections is null)
            {
                return decodeDuration;
            }
            long duration = MemoryMarshal.Cast<byte, long>(corrections.Memory.Span)[sampleIndex];
            return duration < 0 ? null : duration;
        }

        public void Dispose() => corrections?.Dispose();
    }
}
