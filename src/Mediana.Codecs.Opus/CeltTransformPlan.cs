// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus
{
    internal class CeltTransformPlan : IDisposable
    {
        public const int MaximumFourierSize = 480;
        public const int Overlap = 120;
        private const int PhaseOffset = 2 * MaximumFourierSize;
        private const int WindowOffset = PhaseOffset + 1800;
        private const int StorageFloats = WindowOffset + Overlap;
        private const int PermutationCount = 900;
        private readonly MediaBuffer tables;

        private CeltTransformPlan(MediaBuffer tables) => this.tables = tables.Retain();

        public ReadOnlySpan<float> FourierRotations => Values[..PhaseOffset];
        public ReadOnlySpan<float> Window => Values.Slice(WindowOffset, Overlap);

        public ReadOnlySpan<float> Phases(int frameShift)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(frameShift);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frameShift, 3);
            int size = CeltBandTables.ShortTransformSize << frameShift;
            return Values.Slice(PhaseOffset + size - CeltBandTables.ShortTransformSize, size);
        }

        public ReadOnlySpan<ushort> Permutation(int frameShift)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(frameShift);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frameShift, 3);
            int size = (CeltBandTables.ShortTransformSize / 2) << frameShift;
            ReadOnlySpan<ushort> permutations = MemoryMarshal.Cast<byte, ushort>(tables.Memory.Span[(StorageFloats * sizeof(float))..]);
            return permutations.Slice(size - CeltBandTables.ShortTransformSize / 2, size);
        }

        private ReadOnlySpan<float> Values => MemoryMarshal.Cast<byte, float>(tables.Memory.Span[..(StorageFloats * sizeof(float))]);

        public static CeltTransformPlan Create(MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            using WritableMediaBuffer storage = allocator.Allocate(StorageFloats * sizeof(float) + PermutationCount * sizeof(ushort));
            Span<float> values = MemoryMarshal.Cast<byte, float>(storage.Memory.Span[..(StorageFloats * sizeof(float))]);
            Span<ushort> permutations = MemoryMarshal.Cast<byte, ushort>(storage.Memory.Span[(StorageFloats * sizeof(float))..]);
            for (int at = 0; at < MaximumFourierSize; at++)
            {
                (double sine, double cosine) = Math.SinCos(-2 * Math.PI * at / MaximumFourierSize);
                values[2 * at] = (float)cosine;
                values[2 * at + 1] = (float)sine;
            }
            for (int shift = 0; shift < 4; shift++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int size = CeltBandTables.ShortTransformSize << shift;
                Span<float> phase = values.Slice(PhaseOffset + size - CeltBandTables.ShortTransformSize, size);
                for (int at = 0; at < size / 2; at++)
                {
                    (double sine, double cosine) = Math.SinCos(Math.PI * (at + .125) / size);
                    phase[2 * at] = (float)cosine;
                    phase[2 * at + 1] = (float)sine;
                }
                FillPermutation(permutations.Slice((size - CeltBandTables.ShortTransformSize) / 2, size / 2), 0, 0, 1, size / 2);
            }
            for (int at = 0; at < Overlap; at++)
            {
                double sine = Math.Sin(Math.PI * .5 * (at + .5) / Overlap);
                values[WindowOffset + at] = (float)Math.Sin(Math.PI * .5 * sine * sine);
            }
            using MediaBuffer immutable = storage.Publish();
            return new(immutable);
        }

        private static void FillPermutation(Span<ushort> permutation, int destination, int source, int stride, int count)
        {
            if (count == 1)
            {
                permutation[source] = checked((ushort)destination);
                return;
            }
            int radix;
            if (count % 4 == 0) { radix = 4; }
            else if (count % 2 == 0) { radix = 2; }
            else if (count % 3 == 0) { radix = 3; }
            else if (count % 5 == 0) { radix = 5; }
            else { throw new ArgumentException("The CELT Fourier length requires factors of two, three and five.", nameof(count)); }
            for (int part = 0; part < radix; part++)
            {
                FillPermutation(permutation, destination + part * (count / radix), source + part * stride, stride * radix, count / radix);
            }
        }

        public void Dispose() => tables.Dispose();
    }
}
