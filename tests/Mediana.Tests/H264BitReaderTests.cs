// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264BitReaderTests
    {
        [Fact]
        public void ReadsBitsAcrossBytesAndPreservesUnsignedValues()
        {
            H264BitReader bits = new([0xAB, 0xCD, 0xEF, 0x12, 0x34]);
            Assert.Equal(0xAu, bits.ReadBits(4));
            Assert.Equal(0xBCDEF123u, bits.ReadBits(32));
            Assert.Equal(4u, bits.ReadBits(4));
            Assert.True(bits.ByteAligned);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void RemovesValidEmulationPreventionBytes(byte value)
        {
            H264BitReader bits = new([0, 0, 3, value, 0x80]);
            Assert.Equal(0u, bits.ReadBits(16));
            Assert.Equal(value, bits.ReadBits(8));
            Assert.False(bits.MoreRbspData());
            bits.ReadTrailingBits();
        }

        [Theory]
        [InlineData(new byte[] { 0, 0, 0 })]
        [InlineData(new byte[] { 0, 0, 1 })]
        [InlineData(new byte[] { 0, 0, 2 })]
        [InlineData(new byte[] { 0, 0, 3 })]
        [InlineData(new byte[] { 0, 0, 3, 4 })]
        public void RejectsInvalidEmulationPrevention(byte[] data) =>
            Assert.Throws<InvalidDataException>(() => ReadAllBytes(data));

        [Fact]
        public void ReadsGolombValuesAndTrailingBits()
        {
            H264BitReader bits = new([0b10100110, 0b01000010, 0b11000000]);
            Assert.Equal(0, bits.ReadUe());
            Assert.Equal(1, bits.ReadUe());
            Assert.Equal(2, bits.ReadUe());
            Assert.Equal(2, bits.ReadSe());
            Assert.Equal(-2, bits.ReadSe());
            Assert.False(bits.MoreRbspData());
            bits.ReadTrailingBits();
        }

        [Fact]
        public void RejectsMissingStopBitAndTruncatedGolomb()
        {
            Assert.Throws<InvalidDataException>(() => ReadTrailing([0]));
            Assert.Throws<InvalidDataException>(() => ReadGolomb([0]));
            Assert.Throws<InvalidDataException>(() => ReadTrailing([0x81]));
            Assert.Throws<InvalidDataException>(() => ReadTrailing([0x80, 0]));
        }

        private static void ReadAllBytes(byte[] data)
        {
            H264BitReader bits = new(data);
            for (int index = 0; index < data.Length; index++)
            {
                bits.ReadBits(8);
            }
        }

        [Fact]
        public void CabacAlignmentRequiresOnesAndPreservesTheNextByte()
        {
            for (int prefix = 0; prefix < 8; prefix++)
            {
                H264BitReader bits = new([0xff, 0x65]);
                bits.ReadBits(prefix);
                bits.AlignOne();
                Assert.True(bits.ByteAligned);
                Assert.Equal(prefix == 0 ? 0xffu : 0x65u, bits.ReadBits(8));
                if (prefix != 0)
                {
                    Assert.Throws<InvalidDataException>(() => ReadCabacAlignment(prefix));
                }
            }
        }

        private static void ReadCabacAlignment(int prefix)
        {
            H264BitReader bits = new([0xfe]);
            bits.ReadBits(prefix);
            bits.AlignOne();
        }

        private static void ReadTrailing(byte[] data)
        {
            H264BitReader bits = new(data);
            bits.ReadTrailingBits();
        }

        private static void ReadGolomb(byte[] data)
        {
            H264BitReader bits = new(data);
            bits.ReadUe();
        }
    }
}
