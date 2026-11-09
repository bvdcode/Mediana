// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcTileBoundaryTests
    {
        [Fact]
        public async Task PredictionAndDisabledInLoopFilteringCannotCrossTileEdgesInsideOneSlice()
        {
            HevcSliceHeader[] headers = await HevcSliceHeaderTestInput.ReadHeadersAsync(
                Path.Combine(AppContext.BaseDirectory, "Data"), "tiles-uniform8");
            HevcSliceHeader slice = headers[0];
            PicturePartitions regions = new([slice]);
            Assert.Equal(4, slice.EntropySubstreams.Count);
            Assert.False(slice.Picture.LoopFilterAcrossTiles);
            Assert.True(regions.SamePredictionRegion(126, 64, 127, 64));
            Assert.False(regions.SamePredictionRegion(127, 64, 128, 64));
            Assert.False(regions.SamePredictionRegion(64, 127, 64, 128));
            Assert.True(regions.CanFilter(126, 64, 127, 64));
            Assert.False(regions.CanFilter(127, 64, 128, 64));
            Assert.False(regions.CanFilter(64, 127, 64, 128));
        }

        [Fact]
        public void TileEntryPointLengthsCountEncodedEmulationPreventionBytes()
        {
            NalPayload nal = new(new byte[] { 0x26, 1, 0xab, 0, 0, 3, 0, 0xcd, 0xef });
            ArraySegment<byte>[] subsets = nal.Substreams(2, [5]);
            Assert.Equal(new byte[] { 0xab, 0, 0, 0 }, subsets[0].ToArray());
            Assert.Equal(new byte[] { 0xcd, 0xef }, subsets[1].ToArray());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(int.MaxValue)]
        public void EmptyOrOutOfRangeTileEntryPointsAreRejected(int length)
        {
            NalPayload nal = new(new byte[] { 0x26, 1, 0xab, 0xcd, 0xef });
            Assert.Throws<InvalidDataException>(() => nal.Substreams(2, [length]));
        }
    }
}
