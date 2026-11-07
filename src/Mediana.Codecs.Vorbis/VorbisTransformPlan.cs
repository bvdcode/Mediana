// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisTransformPlan : IDisposable
    {
        private readonly MediaBuffer storage;
        private readonly int radixCount;
        public int BlockSize { get; }
        private int FourierSize => BlockSize / 4;
        private ReadOnlySpan<float> Values => MemoryMarshal.Cast<byte, float>(storage.Memory.Span[..(8 * FourierSize * sizeof(float))]);
        public ReadOnlySpan<float> Rotations => Values[..(2 * FourierSize)];
        public ReadOnlySpan<Vector2> PreRotation => MemoryMarshal.Cast<float, Vector2>(Values.Slice(2 * FourierSize, 2 * FourierSize));
        public ReadOnlySpan<Vector2> PostRotation => MemoryMarshal.Cast<float, Vector2>(Values.Slice(4 * FourierSize, 2 * FourierSize));
        public ReadOnlySpan<float> Window => Values[(6 * FourierSize)..];
        public ReadOnlySpan<ushort> Permutation => MemoryMarshal.Cast<byte, ushort>(storage.Memory.Span.Slice(8 * FourierSize * sizeof(float), FourierSize * sizeof(ushort)));
        public ReadOnlySpan<byte> Radices => storage.Memory.Span.Slice(8 * FourierSize * sizeof(float) + FourierSize * sizeof(ushort), radixCount);

        private VorbisTransformPlan(MediaBuffer storage, int blockSize, int radixCount)
        {
            this.storage = storage.Retain(); BlockSize = blockSize; this.radixCount = radixCount;
        }

        public static VorbisTransformPlan Create(int blockSize, MediaBufferAllocator allocator, CancellationToken token = default)
        {
            if (blockSize is < 64 or > 8192 || !BitOperations.IsPow2(blockSize)) { throw new ArgumentOutOfRangeException(nameof(blockSize)); }
            token.ThrowIfCancellationRequested();
            int fourierSize = blockSize / 4, bits = BitOperations.Log2((uint)fourierSize), radixCount = (bits + 1) / 2;
            int floatBytes = 8 * fourierSize * sizeof(float), permutationBytes = fourierSize * sizeof(ushort);
            using WritableMediaBuffer writable = allocator.Allocate(floatBytes + permutationBytes + radixCount);
            Span<float> values = MemoryMarshal.Cast<byte, float>(writable.Span[..floatBytes]);
            for (int index = 0; index < fourierSize; index++)
            {
                (double sine, double cosine) = Math.SinCos(-2 * Math.PI * index / fourierSize);
                values[2 * index] = (float)cosine; values[2 * index + 1] = (float)sine;
                (sine, cosine) = Math.SinCos(-Math.PI * index / (blockSize / 2));
                values[2 * fourierSize + 2 * index] = (float)cosine; values[2 * fourierSize + 2 * index + 1] = (float)sine;
                (sine, cosine) = Math.SinCos(-Math.PI * (index + .25) / (blockSize / 2));
                values[4 * fourierSize + 2 * index] = (float)cosine; values[4 * fourierSize + 2 * index + 1] = (float)sine;
            }
            for (int index = 0; index < blockSize / 2; index++)
            {
                double sine = Math.Sin(Math.PI * (index + .5) / blockSize);
                values[6 * fourierSize + index] = (float)Math.Sin(Math.PI * .5 * sine * sine);
            }
            Span<ushort> permutation = MemoryMarshal.Cast<byte, ushort>(writable.Span.Slice(floatBytes, permutationBytes));
            FillPermutation(permutation, 0, 0, 1, fourierSize);
            Span<byte> radices = writable.Span[(floatBytes + permutationBytes)..];
            radices.Fill(4); if ((bits & 1) != 0) { radices[0] = 2; }
            token.ThrowIfCancellationRequested();
            using MediaBuffer immutable = writable.Publish();
            return new(immutable, blockSize, radixCount);
        }

        private static void FillPermutation(Span<ushort> permutation, int destination, int source, int stride, int count)
        {
            if (count == 1) { permutation[source] = (ushort)destination; return; }
            int radix = count % 4 == 0 ? 4 : 2;
            for (int part = 0; part < radix; part++)
            {
                FillPermutation(permutation, destination + part * (count / radix), source + part * stride, stride * radix, count / radix);
            }
        }

        public void Dispose() => storage.Dispose();
    }
}
