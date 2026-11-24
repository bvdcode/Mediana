// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264CabacEncoderTests
    {
        [Fact]
        public void StreamingOutputFitsIndependentExactIntervalsForEveryState() => H264CabacEncodingTestInput.Verify();

        [Theory]
        [InlineData(0u, "4112992221ffffffffffffffffffffffff11fe80")]
        [InlineData(255u, "411299222200000300000300000300000300000300000fff80")]
        public void RestoredCarryResolvesAcrossThreeOutstandingWords(uint suffix, string expected)
        {
            using H264BitWriter bits = new(new MediaBufferAllocator(128), 128, 0x41);
            H264CabacEncoder encoder = new(bits);
            encoder.WriteBypassBits(0x12abcdef, 32);
            for (int i = 0; i < 3; i++)
            {
                encoder.WriteBypassBits(0xefefefef, 32);
            }
            H264CabacEncoderCheckpoint checkpoint = encoder.CreateCheckpoint();
            H264CabacRateSample rate = encoder.RateSample;
            encoder.WriteBypassBits(suffix ^ 255, 8);
            encoder.WriteTermination(true);
            encoder.Restore(checkpoint);
            Assert.Equal(rate, encoder.RateSample);
            Assert.Equal(128, encoder.BinCount);
            encoder.WriteBypassBits(suffix, 8);
            encoder.WriteTermination(true);
            using MediaBuffer packet = bits.TryFinishCompletedRbsp()!;
            Assert.Equal(Convert.FromHexString(expected), packet.Memory.ToArray());
        }

        [Fact]
        public void EmptyTerminationMatchesTheNormativeFlush()
        {
            MediaBufferAllocator allocator = new(32);
            using H264BitWriter bits = new(allocator, 32, 0x41);
            H264CabacEncoder encoder = new(bits);
            encoder.WriteTermination(true);
            using MediaBuffer packet = bits.TryFinishCompletedRbsp()!;
            Assert.Equal(new byte[] { 0x41, 0xfe, 0x80 }, packet.Memory.ToArray());
            Assert.Equal(1, encoder.BinCount);
            H264CabacContext context = new(0, false);
            Assert.Throws<InvalidOperationException>(() => encoder.WriteBin(false, ref context));
            Assert.Throws<InvalidOperationException>(() => encoder.WriteBypass(false));
            Assert.Throws<InvalidOperationException>(() => encoder.WriteBypassBits(0, 0));
            Assert.Throws<InvalidOperationException>(() => encoder.WriteTermination(false));
        }

        [Fact]
        public void RejectedWritesDoNotAdvanceTheArithmeticEngine()
        {
            MediaBufferAllocator allocator = new(32);
            using H264BitWriter bits = new(allocator, 32, 0x41);
            H264CabacEncoder encoder = new(bits);
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.WriteBypassBits(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.WriteBypassBits(0, 33));
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.WriteBypassBits(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.WriteBypassBits(256, 8));
            Assert.Equal(0, encoder.BinCount);
            encoder.WriteTermination(true);
            using MediaBuffer packet = bits.TryFinishCompletedRbsp()!;
            Assert.Equal(new byte[] { 0x41, 0xfe, 0x80 }, packet.Memory.ToArray());
        }

        [Fact]
        public void UnalignedPayloadCannotStartOrFinishArithmeticCoding()
        {
            MediaBufferAllocator allocator = new(32);
            using H264BitWriter bits = new(allocator, 32, 0x41);
            bits.WriteFlag(true);
            Assert.Throws<InvalidOperationException>(() => new H264CabacEncoder(bits));
            Assert.Throws<InvalidOperationException>(() => bits.TryFinishCompletedRbsp());
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(129, 1)]
        [InlineData(1000, 28)]
        public void ByteStuffingMeetsTheNormativeBinDensityBound(int bins, int words)
        {
            MediaBufferAllocator allocator = new(4096);
            using (H264BitWriter bits = new(allocator, 4096, 0x41, lengthPrefixed: true))
            {
                bits.WriteBits(0xfe80, 16);
                using MediaBuffer packet = bits.TryFinishCabac(bins, 1, 3072)!;
                Assert.Equal(7 + words * 3, packet.Memory.Length);
                Assert.Equal((byte)(packet.Memory.Length - 4), packet.Memory.Span[3]);
                for (int offset = 7; offset < packet.Memory.Length; offset += 3)
                {
                    Assert.Equal(new byte[] { 0, 0, 3 }, packet.Memory.Span.Slice(offset, 3).ToArray());
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
