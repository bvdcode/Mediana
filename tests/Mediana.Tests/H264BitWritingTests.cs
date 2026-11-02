// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264BitWritingTests
    {
        [Fact]
        public void AllAlignmentsWidthsAndEscapesMatchIndependentBitPacking() => H264BitWritingTestInput.Verify();

        [Fact]
        public void InvalidWidthsAndValuesDoNotChangeTheWriter()
        {
            MediaBufferAllocator allocator = new(1024);
            using H264BitWriter writer = new(allocator, 128, 0x65);
            writer.WriteBits(5, 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteBits(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteBits(0, 65));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteBits(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteBits(8, 3));
            Assert.Equal(3, writer.BitPosition);
            writer.WriteBits(0, 0);
            Assert.Equal(3, writer.BitPosition);
            using MediaBuffer packet = writer.Finish();
            Assert.Equal(new byte[] { 0x65, 0xB0 }, packet.Memory.ToArray());
            Assert.Throws<ObjectDisposedException>(() => writer.WriteBits(0, 0));
        }
    }
}
