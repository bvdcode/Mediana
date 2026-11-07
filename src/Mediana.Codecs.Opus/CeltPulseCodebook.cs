// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus
{
    /// <summary>Prepared signed-pulse enumeration for CELT pyramid vector quantization.
    /// Count storage belongs to the supplied allocation budget and is reused across vectors.</summary>
    internal class CeltPulseCodebook : IDisposable
    {
        private const uint Overflow = uint.MaxValue;
        private readonly MediaBuffer counts;
        private readonly int columns;

        private CeltPulseCodebook(MediaBuffer counts, int dimensions, int pulses)
        {
            this.counts = counts.Retain();
            MaximumDimensions = dimensions;
            MaximumPulses = pulses;
            columns = pulses + 1;
        }

        public int MaximumDimensions { get; }
        public int MaximumPulses { get; }

        public static CeltPulseCodebook Create(int maximumDimensions, int maximumPulses,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDimensions);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumPulses);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            int columns = checked(maximumPulses + 1);
            int cells = checked((maximumDimensions + 1) * columns);
            using WritableMediaBuffer storage = allocator.Allocate(checked(cells * sizeof(uint)));
            Span<uint> table = MemoryMarshal.Cast<byte, uint>(storage.Memory.Span);
            table.Clear();
            table[0] = 1;
            for (int dimension = 1; dimension <= maximumDimensions; dimension++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int row = dimension * columns, previous = row - columns;
                table[row] = 1;
                for (int pulse = 1; pulse <= maximumPulses; pulse++)
                {
                    if ((pulse & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                    ulong count = (ulong)table[previous + pulse] + table[row + pulse - 1] + table[previous + pulse - 1];
                    table[row + pulse] = (uint)Math.Min(count, Overflow);
                }
            }
            using MediaBuffer immutable = storage.Publish();
            return new(immutable, maximumDimensions, maximumPulses);
        }

        public bool TryGetSize(int dimensions, int pulses, out uint size)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(dimensions, MaximumDimensions);
            ArgumentOutOfRangeException.ThrowIfNegative(pulses);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(pulses, MaximumPulses);
            size = MemoryMarshal.Cast<byte, uint>(counts.Memory.Span)[dimensions * columns + pulses];
            if (size == Overflow)
            {
                size = 0;
                return false;
            }
            return true;
        }

        public ulong Decode(ref OpusRangeDecoder decoder, scoped Span<int> vector, int pulses) =>
            DecodeRank(decoder.ReadUniform(GetSize(vector.Length, pulses)), vector, pulses);

        public ulong DecodeRank(uint rank, Span<int> vector, int pulses)
        {
            uint size = GetSize(vector.Length, pulses);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rank, size);
            if (pulses == 0)
            {
                vector.Clear();
                return 0;
            }
            ReadOnlySpan<uint> table = MemoryMarshal.Cast<byte, uint>(counts.Memory.Span);
            int remaining = pulses;
            ulong energy = 0;
            for (int at = 0; at < vector.Length - 1; at++)
            {
                if (remaining == 0)
                {
                    vector[at..].Clear();
                    return energy;
                }
                int dimension = vector.Length - at;
                uint previous = table[(dimension - 1) * columns + remaining];
                uint current = table[dimension * columns + remaining];
                uint split = (uint)(((ulong)previous + current) / 2);
                bool negative = rank >= split;
                if (negative) { rank -= split; }
                uint low = split - previous;
                int before = remaining;
                while (low > rank)
                {
                    remaining--;
                    low -= table[(dimension - 1) * columns + remaining];
                }
                int magnitude = before - remaining;
                vector[at] = negative ? -magnitude : magnitude;
                energy += (ulong)magnitude * (uint)magnitude;
                rank -= low;
            }
            vector[^1] = rank == 0 ? remaining : -remaining;
            return energy + (ulong)remaining * (uint)remaining;
        }

        private uint GetSize(int dimensions, int pulses)
        {
            if (!TryGetSize(dimensions, pulses, out uint size))
            {
                throw new InvalidDataException("The CELT pulse codebook exceeds the 32-bit entropy interval.");
            }
            return size;
        }

        public void Dispose() => counts.Dispose();
    }
}
