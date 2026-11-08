// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1TileTests
    {
        [Theory]
        [InlineData("tilesColumns", false)]
        [InlineData("tilesRows", false)]
        [InlineData("tilesGrid", false)]
        [InlineData("tilesGroups", false)]
        [InlineData("tilesNonuniform", false)]
        [InlineData("tilesMono", true)]
        [InlineData("tilesSb128", false)]
        [InlineData("tilesTexture", false)]
        [InlineData("tilesUneven", false)]
        public void ReconstructsIndependentPicturesThroughThePublicFactory(string fixture, bool monochrome) =>
            Av1PictureSequenceTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), fixture,
                monochrome ? [0x81, 0, 28, 0] : [0x81, 0, 12, 0]);

        [Theory]
        [InlineData("tilesColumns", 2, 1)]
        [InlineData("tilesRows", 1, 2)]
        [InlineData("tilesGrid", 2, 2)]
        [InlineData("tilesGroups", 2, 2)]
        [InlineData("tilesNonuniform", 2, 2)]
        [InlineData("tilesMono", 2, 2)]
        [InlineData("tilesSb128", 2, 2)]
        [InlineData("tilesTexture", 2, 2)]
        [InlineData("tilesUneven", 3, 2)]
        public void IndependentHeadersDescribeTheActualGrid(string fixture, int columns, int rows)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", fixture + ".obu"));
            Av1FrameUnitReader units = new(encoded);
            Assert.True(units.TryRead(out ReadOnlySpan<byte> unit, CancellationToken.None));
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            {
                using Av1Picture picture = decoder.Decode(unit);
                Assert.Equal(columns, picture.Header.Tiles.Columns);
                Assert.Equal(rows, picture.Header.Tiles.Rows);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
