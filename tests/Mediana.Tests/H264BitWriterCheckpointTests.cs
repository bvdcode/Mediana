// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    public class H264BitWriterCheckpointTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RbspPositionExcludesHeadersAndPreventionBytes(bool prefixed)
        {
            MediaBufferAllocator allocator = new(1024);
            using H264BitWriter writer = new(allocator, 64, 0x65, lengthPrefixed: prefixed);
            Assert.Equal(0, writer.BitPosition);
            writer.WriteBits(0, 24);
            Assert.Equal(24, writer.BitPosition);
            Assert.Equal((prefixed ? 4 : 0) + 5, writer.EncodedLength);
            writer.WriteBits(5, 3);
            Assert.Equal(27, writer.BitPosition);
            Assert.Equal(27, writer.CreateCheckpoint().BitPosition);
        }

        [Theory]
        [InlineData(32)]
        [InlineData(10000)]
        public void RestorePreservesPartialByteAndEscapingAcrossGrowthAndMeasuredOverflow(int capacity)
        {
            MediaBufferAllocator allocator = new(65536);
            using H264BitWriter writer = new(allocator, capacity, 0x41, lengthPrefixed: true, measureOverflow: true);
            using H264BitWriter expected = new(allocator, capacity, 0x41, lengthPrefixed: true);
            writer.WriteBits(0, 19);
            expected.WriteBits(0, 19);
            H264BitWriterCheckpoint checkpoint = writer.CreateCheckpoint();
            for (int index = 0; index < 12000; index++)
            {
                writer.WriteBits(0, 8);
            }
            Assert.True(writer.EncodedLength > capacity);
            writer.Restore(checkpoint);
            Assert.Equal(19, writer.BitPosition);
            writer.WriteBits(1, 5);
            expected.WriteBits(1, 5);
            writer.WriteUnsigned(17);
            expected.WriteUnsigned(17);
            using MediaBuffer actual = writer.Finish();
            using MediaBuffer reference = expected.Finish();
            Assert.Equal(reference.Memory.ToArray(), actual.Memory.ToArray());
            actual.Dispose();
            reference.Dispose();
            writer.Dispose();
            expected.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void InvalidOrFinalizedCheckpointsCannotChangeTheWriter()
        {
            MediaBufferAllocator allocator = new(1024);
            using H264BitWriter writer = new(allocator, 64, 0x65);
            using H264BitWriter other = new(allocator, 64, 0x65);
            H264BitWriterCheckpoint checkpoint = writer.CreateCheckpoint();
            H264BitWriterCheckpoint foreign = other.CreateCheckpoint();
            writer.WriteBits(5, 3);
            Assert.Throws<ArgumentException>(() => writer.Restore(default));
            Assert.Throws<ArgumentException>(() => writer.Restore(foreign));
            Assert.Equal(3, writer.BitPosition);
            writer.Restore(checkpoint);
            Assert.Throws<ArgumentException>(() => writer.Restore(checkpoint));
            Assert.Equal(0, writer.BitPosition);
            H264BitWriterCheckpoint current = writer.CreateCheckpoint();
            using MediaBuffer packet = writer.Finish();
            Assert.Throws<ObjectDisposedException>(() => writer.Restore(current));
            Assert.Throws<ObjectDisposedException>(() => writer.CreateCheckpoint());
        }
    }
}
