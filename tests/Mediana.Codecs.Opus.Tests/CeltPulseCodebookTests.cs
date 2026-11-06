// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltPulseCodebookTests
    {
        [Fact]
        public void PulseCoefficientsAndEnergyMatchIndependentReference() =>
            Assert.Equal(13574, OpusPulseTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void CountsAndOverflowMatchExactCombinatorics()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (CeltPulseCodebook codebook = CeltPulseCodebook.Create(256, 256, allocator))
            {
                for (int dimensions = 1; dimensions <= 256; dimensions++)
                {
                    foreach (int pulses in new int[] { 0, 1, 2, 3, 4, 5, 10, 31, 64, 128, 256 })
                    {
                        BigInteger count = CountExactly(dimensions, pulses);
                        bool fits = codebook.TryGetSize(dimensions, pulses, out uint size);
                        Assert.Equal(count < uint.MaxValue, fits);
                        Assert.Equal(fits ? (uint)count : 0, size);
                    }
                }
                Assert.Equal(257 * 257 * sizeof(uint), allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ZeroPulsesAndSingleDimensionHaveExactDegenerateVectors()
        {
            MediaBufferAllocator allocator = new(4096);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(4, 16, allocator);
            int[] vector = [7, 8, 9];
            Assert.Equal(0UL, codebook.DecodeRank(0, vector, 0));
            Assert.Equal(new int[3], vector);
            Assert.Equal(256UL, codebook.DecodeRank(0, vector.AsSpan(0, 1), 16));
            Assert.Equal(16, vector[0]);
            Assert.Equal(256UL, codebook.DecodeRank(1, vector.AsSpan(0, 1), 16));
            Assert.Equal(-16, vector[0]);
            OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
            Assert.Equal(256UL, codebook.Decode(ref decoder, vector.AsSpan(0, 1), 16));
            Assert.Equal(16, vector[0]);
        }

        [Fact]
        public void InvalidRanksAndUnsupportedCountsDoNotChangeTheDestination()
        {
            MediaBufferAllocator allocator = new(4096);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(16, 16, allocator);
            int[] vector = [9, 8, 7, 6];
            Assert.True(codebook.TryGetSize(4, 2, out uint size));
            Assert.Throws<ArgumentOutOfRangeException>(() => codebook.DecodeRank(size, vector, 2));
            Assert.Equal(new int[] { 9, 8, 7, 6 }, vector);
            int[] wide = Enumerable.Repeat(17, 16).ToArray();
            Assert.Throws<InvalidDataException>(() => codebook.DecodeRank(0, wide, 16));
            Assert.All(wide, value => Assert.Equal(17, value));
            Assert.Throws<ArgumentOutOfRangeException>(() => codebook.DecodeRank(0, [], 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => codebook.DecodeRank(0, vector, 17));
            Assert.Throws<ArgumentOutOfRangeException>(() => codebook.DecodeRank(0, vector, -1));
        }

        [Fact]
        public void ReusedDecodingAllocatesNoStorageAndLeavesTheBudgetStable()
        {
            MediaBufferAllocator allocator = new(32768);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 8, allocator);
            Span<int> vector = stackalloc int[176];
            Assert.True(codebook.TryGetSize(vector.Length, 3, out uint count));
            codebook.DecodeRank(0, vector, 3);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long budget = allocator.AllocatedBytes;
            ulong checksum = 0;
            for (uint index = 0; index < 10000; index++)
            {
                checksum += codebook.DecodeRank(index % count, vector, 3);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(budget, allocator.AllocatedBytes);
            Assert.InRange(checksum, 30000UL, 90000UL);
        }

        [Fact]
        public void SharedReadOnlyTableSupportsConcurrentDecoders()
        {
            MediaBufferAllocator allocator = new(32768);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 8, allocator);
            Parallel.For(0, 4, worker =>
            {
                Span<int> vector = stackalloc int[176];
                for (int index = worker; index < 1000; index += 4)
                {
                    codebook.DecodeRank((uint)index, vector, 3);
                    int norm = 0;
                    foreach (int value in vector) { norm += Math.Abs(value); }
                    Assert.Equal(3, norm);
                }
            });
        }

        [Fact]
        public void AllocationLimitsAndCancellationDoNotLeakStorage()
        {
            MediaBufferAllocator allocator = new(4096);
            Assert.Throws<MediaMemoryLimitException>(() => CeltPulseCodebook.Create(256, 256, allocator));
            Assert.Throws<OperationCanceledException>(() => CeltPulseCodebook.Create(256, 256, allocator, new(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(4, 4, allocator);
            codebook.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<ObjectDisposedException>(() => codebook.TryGetSize(1, 1, out _));
        }

        private static BigInteger CountExactly(int dimensions, int pulses)
        {
            if (pulses == 0) { return BigInteger.One; }
            BigInteger total = 0, positions = 1, partition = 1;
            for (int occupied = 1; occupied <= Math.Min(dimensions, pulses); occupied++)
            {
                positions = positions * (dimensions + 1 - occupied) / occupied;
                if (occupied > 1) { partition = partition * (pulses + 1 - occupied) / (occupied - 1); }
                total += (positions * partition) << occupied;
            }
            return total;
        }
    }
}
