// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264MacroblockGridTests
    {
        [Theory]
        [InlineData(false, false, 8, -1, 0, 6, 0)]
        [InlineData(false, true, 8, -1, 1, 7, 0)]
        [InlineData(false, true, 8, -1, 8, 6, 4)]
        [InlineData(false, true, 8, -1, 15, 7, 7)]
        [InlineData(false, false, 8, 0, -1, 3, 15)]
        [InlineData(false, true, 8, 0, -1, 3, 15)]
        [InlineData(false, false, 8, -1, -1, 1, 15)]
        [InlineData(false, true, 8, -1, -1, 1, 15)]
        [InlineData(false, true, 8, 16, -1, 5, 15)]
        [InlineData(false, false, 9, -1, 0, 7, 0)]
        [InlineData(false, true, 9, -1, 0, 6, 8)]
        [InlineData(false, true, 9, -1, 15, 7, 15)]
        [InlineData(false, false, 9, 0, -1, 8, 15)]
        [InlineData(false, true, 9, 0, -1, 8, 15)]
        [InlineData(false, false, 9, -1, -1, 6, 15)]
        [InlineData(false, true, 9, -1, -1, 7, 7)]
        [InlineData(true, false, 8, -1, 7, 6, 14)]
        [InlineData(true, false, 8, -1, 8, 7, 0)]
        [InlineData(true, true, 8, -1, 15, 6, 15)]
        [InlineData(true, false, 8, 0, -1, 3, 14)]
        [InlineData(true, true, 8, 0, -1, 2, 15)]
        [InlineData(true, false, 8, -1, -1, 1, 14)]
        [InlineData(true, true, 8, -1, -1, 0, 15)]
        [InlineData(true, false, 8, 16, -1, 5, 14)]
        [InlineData(true, true, 8, 16, -1, 4, 15)]
        [InlineData(true, false, 9, -1, 0, 6, 1)]
        [InlineData(true, true, 9, -1, 0, 7, 0)]
        [InlineData(true, false, 9, -1, 7, 6, 15)]
        [InlineData(true, false, 9, -1, 8, 7, 1)]
        [InlineData(true, true, 9, -1, 8, 7, 8)]
        [InlineData(true, false, 9, 0, -1, 3, 15)]
        [InlineData(true, true, 9, 0, -1, 3, 15)]
        [InlineData(true, true, 9, 16, -1, 5, 15)]
        public void NeighboursMatchNormativeFrameFieldCases(bool currentField, bool neighbourField, int current,
            int x, int y, int address, int localY)
        {
            H264MacroblockGrid grid = Create();
            for (int pair = 0; pair < 12; pair += 2)
            {
                grid.SetFieldPair(pair, pair == 8 ? currentField : neighbourField);
            }
            H264NeighbourLocation actual = Assert.IsType<H264NeighbourLocation>(grid.Neighbour(current, x, y, 16, 16, 0));
            Assert.Equal(address, actual.Address);
            Assert.Equal(localY, actual.Y);
            Assert.Equal((x + 16) % 16, actual.X);
        }

        [Theory]
        [InlineData(16, 16)]
        [InlineData(8, 8)]
        [InlineData(8, 16)]
        public void MixedPairsCoverEveryPlaneSampleExactlyOnce(int width, int height)
        {
            for (int mask = 0; mask < 64; mask++)
            {
                H264MacroblockGrid grid = Create();
                byte[] visits = new byte[12 * width * height];
                for (int pair = 0; pair < 6; pair++)
                {
                    grid.SetFieldPair(pair * 2, (mask & (1 << pair)) != 0);
                }
                for (int address = 0; address < 12; address++)
                {
                    (int logicalX, int logicalY) = grid.Position(address);
                    Assert.Equal(address, grid.Address(logicalX, logicalY));
                    (int x, int y, int step) = grid.PixelOrigin(address, width, height);
                    for (int row = 0; row < height; row++)
                    {
                        for (int column = 0; column < width; column++)
                        {
                            visits[(y + row * step) * width * 3 + x + column]++;
                        }
                    }
                }
                Assert.All(visits, value => Assert.Equal(1, value));
            }
        }

        [Fact]
        public void UnavailableNeighboursDoNotReadAnUndecodedPairMode()
        {
            H264MacroblockGrid grid = Create();
            grid.SetFieldPair(8, false);
            Assert.Null(grid.Neighbour(9, 16, -1, 16, 16, 8));
            Assert.Null(grid.Neighbour(8, -1, 0, 16, 16, 8));
            Assert.Null(grid.Neighbour(8, 0, -1, 16, 16, 8));
            Assert.Null(grid.Neighbour(8, 0, 16, 16, 16, 8));
            Assert.Equal(new H264NeighbourLocation(8, 0, 15), grid.Neighbour(9, 0, -1, 16, 16, 8));
        }

        [Fact]
        public void PairModeMustBeKnownAndCannotChangeWithinThePair()
        {
            H264MacroblockGrid grid = Create();
            Assert.Throws<InvalidDataException>(() => grid.IsField(0));
            Assert.Throws<ArgumentException>(() => grid.SetFieldPair(1, true));
            grid.SetFieldPair(0, true);
            Assert.True(grid.IsField(0));
            Assert.True(grid.IsField(1));
            Assert.Throws<InvalidDataException>(() => grid.SetFieldPair(0, false));
        }

        private static H264MacroblockGrid Create()
        {
            H264SequenceParameterSet sequence = new() { WidthInMbs = 3, HeightInMapUnits = 2, MbAdaptiveFrameField = true };
            H264PictureLayout layout = new(sequence, H264PictureStructure.Frame);
            return new(layout, 3, new byte[6]);
        }
    }
}
