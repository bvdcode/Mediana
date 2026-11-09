// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1TileSyntaxTests
    {
        [Theory]
        [InlineData("1")]
        [InlineData("0")]
        public void SingleTileHasNoGridAllocation(string encoded)
        {
            Av1SequenceHeader sequence = Sequence(48, 40);
            Av1BitReader bits = Reader(encoded + "101");
            Av1TileInfo info = Av1TileInfo.Read(ref bits, sequence);
            Assert.Null(info.Grid);
            Assert.Equal(1, info.Count);
            Assert.Equal(new Av1TileBounds(0, 10, 0, 12), info.Bounds(0, sequence));
            Assert.Equal(5, bits.Read(3));
        }

        [Fact]
        public void UniformTilesClipFinalBoundsAndSelectEntropyContext()
        {
            Av1SequenceHeader sequence = Sequence(192, 160);
            Av1BitReader bits = Reader("1" + "10" + "10" + "01" + "10" + "101");
            Av1TileInfo info = Av1TileInfo.Read(ref bits, sequence);
            Assert.Equal(2, info.Columns);
            Assert.Equal(2, info.Rows);
            Assert.Equal(1, info.ContextUpdateTile);
            Assert.Equal(3, info.SizeBytes);
            Assert.Equal(new Av1TileBounds(0, 32, 0, 32), info.Bounds(0, sequence));
            Assert.Equal(new Av1TileBounds(32, 40, 32, 48), info.Bounds(3, sequence));
            Assert.Equal(5, bits.Read(3));
        }

        [Fact]
        public void NonuniformTilesReadIndependentSuperblockSizes()
        {
            Av1SequenceHeader sequence = Sequence(192, 192);
            Av1BitReader bits = Reader("0" + "01" + "01" + "11" + "00" + "101");
            Av1TileInfo info = Av1TileInfo.Read(ref bits, sequence);
            Assert.Equal(4, info.Count);
            Assert.Equal(3, info.ContextUpdateTile);
            Assert.Equal(1, info.SizeBytes);
            Assert.Equal(new Av1TileBounds(0, 16, 0, 16), info.Bounds(0, sequence));
            Assert.Equal(new Av1TileBounds(16, 48, 16, 48), info.Bounds(3, sequence));
            Assert.Equal(5, bits.Read(3));
        }

        [Fact]
        public void ActualUniformTileCountMayBeSmallerThanTheSignaledPowerOfTwo()
        {
            Av1SequenceHeader sequence = Sequence(320, 64);
            Av1BitReader bits = Reader("1" + "110" + "10" + "00" + "101");
            Av1TileInfo info = Av1TileInfo.Read(ref bits, sequence);
            Assert.Equal(2, info.ColumnLog2);
            Assert.Equal(3, info.Columns);
            Assert.Equal(1, info.Rows);
            Assert.Equal(2, info.ContextUpdateTile);
            Assert.Equal(new Av1TileBounds(0, 16, 64, 80), info.Bounds(2, sequence));
            Assert.Equal(5, bits.Read(3));
            Assert.Throws<InvalidDataException>(() => ReadInfo("1" + "110" + "11" + "00", sequence));
        }

        [Fact]
        public void MinimumWidthSplitAnd128SuperblockBoundsFollowTheSequence()
        {
            Av1SequenceHeader wide = Sequence(8192, 64);
            Av1TileInfo split = ReadInfo("1" + "0" + "0" + "00", wide);
            Assert.Equal(2, split.Columns);
            Assert.Equal(new Av1TileBounds(0, 16, 1024, 2048), split.Bounds(1, wide));
            Av1SequenceHeader large = Sequence(320, 272) with { Superblock128 = true };
            Av1TileInfo grid = ReadInfo("1" + "10" + "10" + "10" + "00", large);
            Assert.Equal(new Av1TileBounds(64, 68, 64, 80), grid.Bounds(3, large));
            Assert.Throws<InvalidDataException>(() => ReadInfo(new string('0', 1024), Sequence(4160, 64)));
        }

        [Fact]
        public void GroupsReadLittleEndianLengthsAndUseTheRemainingFinalPayload()
        {
            Av1TileInfo info = Grid();
            byte[] data = [0x88, 1, 0, 0x21, 0x22, 0x23];
            Av1TileGroupReader group = new(data, info, 0, false);
            Assert.True(group.TryRead(out int first, out ReadOnlySpan<byte> a));
            Assert.Equal(0, first);
            Assert.Equal(new byte[] { 0x21, 0x22 }, a.ToArray());
            Assert.True(group.TryRead(out int second, out ReadOnlySpan<byte> b));
            Assert.Equal(1, second);
            Assert.Equal(new byte[] { 0x23 }, b.ToArray());
            Assert.False(group.TryRead(out _, out _));
            Assert.Equal(2, group.NextTile);
            Av1TileGroupReader next = new([0xD8, 0, 0, 0x31, 0x32], info, group.NextTile, false);
            Assert.True(next.TryRead(out int third, out _));
            Assert.Equal(2, third);
            Assert.True(next.TryRead(out int fourth, out _));
            Assert.Equal(3, fourth);
            Assert.False(next.TryRead(out _, out _));
            Assert.Equal(4, next.NextTile);
        }

        [Theory]
        [InlineData(new byte[] { 0x88, 0, 0, 1, 2 }, 0, true)]
        [InlineData(new byte[] { 0xD8, 0, 0, 1, 2 }, 0, false)]
        [InlineData(new byte[] { 0xC8, 0, 0, 1, 2 }, 2, false)]
        [InlineData(new byte[] { 0x89, 0, 0, 1, 2 }, 0, false)]
        [InlineData(new byte[] { 0x88, 0 }, 0, false)]
        [InlineData(new byte[] { 0x88, 255, 255, 1 }, 0, false)]
        [InlineData(new byte[] { 0x88, 0, 0, 1 }, 0, false)]
        public void RejectsInvalidRangesAlignmentAndPayloadExtents(byte[] encoded, int expectedTile, bool frameObu) =>
            Assert.Throws<InvalidDataException>(() => ReadGroup(encoded, Grid(), expectedTile, frameObu));

        [Fact]
        public void FourByteTileSizeCannotWrapToZero()
        {
            Av1TileInfo info = Grid() with { SizeBytes = 4 };
            Assert.Throws<InvalidDataException>(() => ReadGroup([0x88, 255, 255, 255, 255, 1], info, 0, false));
        }

        [Fact]
        public void FullFrameGroupBorrowsTheOriginalTilePayloads()
        {
            byte[] encoded = [0, 0, 0, 10, 0, 0, 20, 0, 0, 30, 40];
            Av1TileGroupReader group = new(encoded, Grid(), 0, true);
            Assert.True(group.TryRead(out int index, out ReadOnlySpan<byte> first));
            Assert.Equal(0, index);
            Assert.Equal(10, first[0]);
            encoded[3] = 11;
            Assert.Equal(11, first[0]);
            int count = 1;
            while (group.TryRead(out _, out _)) { count++; }
            Assert.Equal(4, count);
            Assert.Equal(4, group.NextTile);
        }

        [Fact]
        public void SeparateGroupObusCoverTheGridWithoutCopyingPayloads()
        {
            byte[] encoded = [0x22, 6, 0x88, 1, 0, 10, 11, 12, 0x22, 5, 0xD8, 0, 0, 13, 14];
            Av1TilePayloadReader tiles = new(encoded, Grid(), false);
            int count = 0;
            while (tiles.TryRead(out int index, out ReadOnlySpan<byte> payload, CancellationToken.None))
            {
                Assert.Equal(count, index);
                Assert.False(payload.IsEmpty);
                if (index == 0)
                {
                    encoded[5] = 21;
                    Assert.Equal(21, payload[0]);
                }
                count++;
            }
            Assert.Equal(4, count);
        }

        [Theory]
        [InlineData(new byte[] { 0x22, 6, 0x88, 1, 0, 10, 11, 12 })]
        [InlineData(new byte[] { 0x32, 1, 0 })]
        [InlineData(new byte[] { 0x22, 6, 0x88, 1, 0, 10, 11, 12, 0x22, 5, 0x88, 0, 0, 13, 14 })]
        public void SeparateGroupsRequireCompleteOrderedCoverage(byte[] encoded) =>
            Assert.Throws<InvalidDataException>(() => ReadPayloads(encoded, CancellationToken.None));

        [Fact]
        public void PayloadEnumerationChecksCancellationBeforeReadingSyntax() =>
            Assert.Throws<OperationCanceledException>(() => ReadPayloads([], new(true)));

        private static Av1TileInfo Grid() => new(1, 1, 0, 2, new([0, 16, 32], [0, 16, 32]));

        private static void ReadGroup(byte[] encoded, Av1TileInfo info, int expectedTile, bool frameObu)
        {
            Av1TileGroupReader group = new(encoded, info, expectedTile, frameObu);
            while (group.TryRead(out _, out _)) { }
        }

        private static void ReadPayloads(byte[] encoded, CancellationToken cancellationToken)
        {
            Av1TilePayloadReader reader = new(encoded, Grid(), false);
            while (reader.TryRead(out _, out _, cancellationToken)) { }
        }

        private static Av1TileInfo ReadInfo(string encoded, Av1SequenceHeader sequence)
        {
            Av1BitReader bits = Reader(encoded);
            return Av1TileInfo.Read(ref bits, sequence);
        }

        private static Av1SequenceHeader Sequence(int width, int height) =>
            new(width, height, false, false, 0, false, false, false, false, false, false, false, false, 2, 2, 2, default);

        private static Av1BitReader Reader(string encoded)
        {
            byte[] bytes = new byte[(encoded.Length + 7) / 8];
            for (int index = 0; index < encoded.Length; index++)
            {
                if (encoded[index] == '1') { bytes[index >> 3] |= (byte)(1 << (7 - (index & 7))); }
            }
            return new(bytes);
        }
    }
}
