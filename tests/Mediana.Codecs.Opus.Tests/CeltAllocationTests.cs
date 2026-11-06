// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltAllocationTests
    {
        [Fact]
        public void AllocationFineBitsStereoAndEntropyMatchIndependentReference() =>
            Assert.Equal(4096, OpusAllocationTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void PlanningUsesOnlyCallerProvidedScratchAndOutputStorage()
        {
            Span<int> shape = stackalloc int[21], fine = stackalloc int[21], boosts = stackalloc int[21];
            Span<byte> priorities = stackalloc byte[21];
            boosts.Clear();
            CeltBandMode mode = new(2, 3, 0, 21);
            CeltAllocationInfo result = default;
            OpusRangeDecoder warm = new(ReadOnlySpan<byte>.Empty);
            CeltAllocationReader.Read(ref warm, mode, boosts, 0, 5, new(shape, fine, priorities));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
                result = CeltAllocationReader.Read(ref decoder, mode, boosts, 0, index % 11, new(shape, fine, priorities));
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(1, result.CodedBands);
            Assert.Equal(0, result.BalanceEighthBits);
            foreach (int value in shape) { Assert.Equal(0, value); }
            foreach (int value in fine) { Assert.Equal(0, value); }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void InvalidBudgetsAndModesLeaveCallerStorageUntouched(int kind) =>
            Assert.ThrowsAny<ArgumentException>(() => ReadInvalid(kind));

        private static void ReadInvalid(int kind)
        {
            int[] shape = Enumerable.Repeat(77, 21).ToArray();
            int[] fine = Enumerable.Repeat(66, 21).ToArray();
            byte[] priorities = Enumerable.Repeat((byte)55, 21).ToArray();
            int[] boosts = new int[21];
            CeltBandMode mode = new(1, 0, 0, 21);
            int available = 0, trim = 5;
            switch (kind)
            {
                case 0: mode = default; break;
                case 1: available = 100; break;
                case 2: trim = 11; break;
                case 3: boosts[20] = -1; break;
                default: throw new InvalidOperationException("The invalid allocation case is absent.");
            }
            try
            {
                OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
                CeltAllocationReader.Read(ref decoder, mode, boosts, available, trim, new(shape, fine, priorities));
            }
            finally
            {
                Assert.All(shape, value => Assert.Equal(77, value));
                Assert.All(fine, value => Assert.Equal(66, value));
                Assert.All(priorities, value => Assert.Equal(55, value));
            }
        }
    }
}
