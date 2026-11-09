// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcTileLayoutTests
    {
        [Theory]
        [InlineData("0100101", 2, 2)]
        [InlineData("010010011", 1, 1)]
        public async Task PpsTileSyntaxDerivesUniformAndExplicitGeometry(string bits, int firstWidth, int firstHeight)
        {
            HevcSliceHeader[] slices = await Mediana.Tests.HevcSliceHeaderTestInput.ReadHeadersAsync(
                Path.Combine(AppContext.BaseDirectory, "Data"), "tiles-uniform8");
            HevcTileLayout layout = HevcTileLayout.Read(new(ToBytes(bits)), slices[0].Picture.Sequence, true);
            Assert.Equal(new HevcTile(0, 0, firstWidth, firstHeight), layout.TileAt(0));
            Assert.Equal(20, layout.Count);
        }

        [Theory]
        [InlineData("00110")]
        [InlineData("01000101")]
        [InlineData("010010000101")]
        [InlineData("0100100100100")]
        public async Task PpsTileSyntaxRejectsTooManyTilesAndExplicitDimensionsThatConsumeLaterTiles(string bits)
        {
            HevcSliceHeader[] slices = await Mediana.Tests.HevcSliceHeaderTestInput.ReadHeadersAsync(
                Path.Combine(AppContext.BaseDirectory, "Data"), "tiles-uniform8");
            Assert.Throws<InvalidDataException>(() => HevcTileLayout.Read(new(ToBytes(bits)), slices[0].Picture.Sequence, true));
        }

        [Fact]
        public void UniformTileScanVisitsCompleteTilesBeforeTheNextColumnOrRow()
        {
            HevcTileLayout layout = new(5, 4, [2, 3], [2, 2], true);
            int[] expected = [0, 1, 5, 6, 2, 3, 4, 7, 8, 9, 10, 11, 15, 16, 12, 13, 14, 17, 18, 19];
            Assert.Equal(expected, Enumerable.Range(0, layout.Count).Select(layout.ToRaster));
            Assert.Equal(4, layout.MaximumSubstreams(false));
            Assert.Equal(8, layout.MaximumSubstreams(true));
            for (int address = 0; address < expected.Length; address++)
            {
                Assert.Equal(address, layout.ToTileScan(expected[address]));
            }
            Assert.Equal(new HevcTile(2, 0, 3, 2), layout.TileAt(8));
            Assert.False(layout.StartsTile(8));
            Assert.True(layout.StartsTile(12));
            Assert.False(layout.StartsSubstream(5, false));
            Assert.True(layout.StartsSubstream(5, true));
        }

        [Fact]
        public void NonuniformAndSingleCtbTilesKeepEveryRasterAddressAndSubsetBoundary()
        {
            for (int left = 1; left < 5; left++)
            {
                for (int top = 1; top < 4; top++)
                {
                    HevcTileLayout layout = new(5, 4, [left, 5 - left], [top, 4 - top], true);
                    int[] raster = Enumerable.Range(0, layout.Count).Select(layout.ToRaster).ToArray();
                    Assert.Equal(Enumerable.Range(0, 20), raster.Order());
                    Assert.Equal(4, raster.Count(layout.StartsTile));
                    Assert.Equal(8, raster.Count(address => layout.StartsSubstream(address, true)));
                    Assert.Equal(Enumerable.Range(0, 20), raster.Select(layout.ToTileScan));
                    Assert.Equal(raster.Select(layout.TileId).Order(), raster.Select(layout.TileId));
                }
            }
        }

        [Fact]
        public void DisabledTilesRetainRasterOrderAndPictureWideWavefrontRows()
        {
            HevcTileLayout layout = new(5, 4, [5], [4], false);
            Assert.Equal(Enumerable.Range(0, 20), Enumerable.Range(0, 20).Select(layout.ToRaster));
            Assert.Equal(Enumerable.Range(0, 20), Enumerable.Range(0, 20).Select(layout.ToTileScan));
            Assert.Equal(1, layout.MaximumSubstreams(false));
            Assert.Equal(4, layout.MaximumSubstreams(true));
            Assert.All(Enumerable.Range(0, 20), address => Assert.Equal(0, layout.TileId(address)));
        }

        [Theory]
        [InlineData(0, 5, 2, 2)]
        [InlineData(-1, 6, 2, 2)]
        [InlineData(2, 2, 2, 2)]
        [InlineData(2, 3, 0, 4)]
        [InlineData(2, 3, 1, 4)]
        public void InvalidTileDimensionsCannotLeaveHolesOverlapOrEmptyTiles(int left, int right, int top, int bottom)
        {
            Assert.Throws<InvalidDataException>(() => new HevcTileLayout(5, 4, [left, right], [top, bottom], true));
        }

        private static byte[] ToBytes(string bits)
        {
            byte[] bytes = new byte[(bits.Length + 7) / 8];
            for (int index = 0; index < bits.Length; index++)
            {
                if (bits[index] == '1') { bytes[index / 8] |= (byte)(1 << (7 - index % 8)); }
            }
            return bytes;
        }
    }
}
