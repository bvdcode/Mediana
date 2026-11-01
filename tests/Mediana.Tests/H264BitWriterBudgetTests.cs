// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    public class H264BitWriterBudgetTests
    {
        [Theory]
        [InlineData(0UL)]
        [InlineData(1UL)]
        [InlineData(65535UL)]
        public void OverflowMeasurementCountsExactEscapedOutputWithoutAllocatingBeyondCapacity(ulong pattern)
        {
            MediaBufferAllocator allocator = new(65536);
            using H264BitWriter complete = new(allocator, 16384, 0x65, lengthPrefixed: true);
            using H264BitWriter bounded = new(allocator, 16, 0x65, lengthPrefixed: true, measureOverflow: true);
            for (int index = 0; index < 100; index++)
            {
                complete.WriteBits(pattern, 16);
                bounded.WriteBits(pattern, 16);
            }
            using MediaBuffer expected = complete.Finish();
            Assert.Null(bounded.TryFinish());
            Assert.Equal(expected.Memory.Length, bounded.EncodedLength);
            complete.Dispose();
            expected.Dispose();
            Assert.Equal(16, allocator.AllocatedBytes);
            bounded.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ExplicitMeasurementPreservesSuccessfulBytesAndDefaultOverflowStillFails()
        {
            MediaBufferAllocator allocator = new(65536);
            using H264BitWriter measured = new(allocator, 16, 0x41, lengthPrefixed: true, measureOverflow: true);
            using H264BitWriter original = new(allocator, 16, 0x41, lengthPrefixed: true);
            measured.WriteUnsigned(17);
            original.WriteUnsigned(17);
            using MediaBuffer expected = original.Finish();
            using MediaBuffer actual = measured.TryFinish()!;
            Assert.Equal(expected.Memory.ToArray(), actual.Memory.ToArray());
            using H264BitWriter small = new(allocator, 8, 0x65);
            Assert.Throws<NotSupportedException>(() => small.WriteBits(ulong.MaxValue, 64));
        }
    }
}
