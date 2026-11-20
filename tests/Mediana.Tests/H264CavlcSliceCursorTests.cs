// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264CavlcSliceCursorTests
    {
        [Fact]
        public void ResolvesSkippedPairModesBeforeReconstructionWithoutConsumingPayload() => H264CavlcSliceTestInput.Verify();

        [Theory]
        [InlineData(1, 4)]
        [InlineData(0, 3)]
        public void RejectsPartialAdaptivePairs(int first, int end)
        {
            H264PictureLayout layout = new(new() { WidthInMbs = 2, HeightInMapUnits = 1, MbAdaptiveFrameField = true }, H264PictureStructure.Frame);
            Assert.Throws<InvalidDataException>(() => new H264CavlcSliceCursor(layout, H264SliceKind.P, first, end));
        }

        [Fact]
        public void RejectsTruncatedAndExcessiveSkipRuns()
        {
            Assert.Throws<InvalidDataException>(() => ReadSlice([0x80]));
            Assert.Throws<InvalidDataException>(() => ReadSlice([0x30]));
            Assert.Throws<InvalidDataException>(() => ReadSlice([0x70]));
        }

        private static void ReadSlice(byte[] data)
        {
            H264PictureLayout layout = new(new() { WidthInMbs = 2, HeightInMapUnits = 1, MbAdaptiveFrameField = true }, H264PictureStructure.Frame);
            H264MacroblockGrid grid = new(layout, 2, new byte[2]);
            H264CavlcSliceCursor cursor = new(layout, H264SliceKind.P, 0, 4);
            H264BitReader bits = new(data);
            while (cursor.MoveNext(ref bits, grid, out _, out bool skipped))
            {
                if (!skipped)
                {
                    bits.ReadUe(25);
                }
            }
            bits.ReadTrailingBits();
        }
    }
}
