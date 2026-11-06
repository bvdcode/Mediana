// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltSplitTests
    {
        [Fact]
        public void SplitsAndIntegerApproximationsMatchTheIndependentDecoder() =>
            Assert.Equal(8105, OpusSplitTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void SplitReadingDoesNotAllocatePerBand()
        {
            byte[] bytes = new byte[32];
            CeltSplitMode mode = new(20, 1, 44, 1, 1, false, 21);
            OpusRangeDecoder warm = new(bytes);
            CeltSplitAngleReader.Read(ref warm, mode, 800, 1200, 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int total = 0;
            for (int at = 0; at < 10000; at++)
            {
                OpusRangeDecoder decoder = new(bytes);
                total += CeltSplitAngleReader.Read(ref decoder, mode, 800, 1200, 1).Angle;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(0, total);
        }

        [Fact]
        public void DisablingInversionStillConsumesItsEncodedFlag()
        {
            byte[] bytes = Enumerable.Repeat((byte)255, 32).ToArray();
            OpusRangeDecoder enabled = new(bytes), disabled = new(bytes);
            CeltSplitMode mode = new(20, 1, 44, 1, 1, true, 0);
            CeltSplitAngle first = CeltSplitAngleReader.Read(ref enabled, mode, 32, 32, 3);
            CeltSplitAngle second = CeltSplitAngleReader.Read(ref disabled, mode with { DisableInversion = true }, 32, 32, 3);
            Assert.True(first.Inverted);
            Assert.False(second.Inverted);
            Assert.Equal(enabled.Range, disabled.Range);
            Assert.Equal(enabled.EighthBitsUsed, disabled.EighthBitsUsed);
        }
    }
}
