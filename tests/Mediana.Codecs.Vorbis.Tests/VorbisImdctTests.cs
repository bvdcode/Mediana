// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Vorbis.Tests
{
    public class VorbisImdctTests
    {
        [Fact]
        public void EveryBlockSizeMatchesIndependentReferenceWithoutWorkingAllocations() =>
            Assert.Equal(128, VorbisImdctTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), true));

        [Theory]
        [InlineData(32)]
        [InlineData(65)]
        [InlineData(16384)]
        public void InvalidSizesReserveNoStorage(int size)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            Assert.Throws<ArgumentOutOfRangeException>(() => VorbisTransformPlan.Create(size, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void PlanAllocationHonorsTheCallerBudget()
        {
            MediaBufferAllocator allocator = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => VorbisTransformPlan.Create(8192, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
